using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BunnyLoader;

/// <summary>
/// Reescrita de IL dos mods PC (Mono.Cecil).
///
/// Um mod compilado no PC acessa os campos do jogo com <c>ldfld/stfld</c>
/// (<c>Terraria.Item::damage</c>, <c>Terraria.Entity::width</c>...). Aqui o
/// jogo existe só como PROXIES do ProxyGen: os campos viraram PROPRIEDADES
/// (<c>ref int damage</c> / getters que criam o proxy da classe), e o JIT do
/// Mono não tem como ligar um campo a uma propriedade — a primeira leitura
/// explode com <c>MissingFieldException</c>.
///
/// A tradução é mecânica e exata na pilha de avaliação:
/// <list type="bullet">
/// <item><c>ldfld/ldsfld</c> -> <c>call(get_X)</c>; se o getter devolve
/// <c>T&amp;</c> (ref property), segue um <c>call Read&lt;T&gt;</c> que
/// descarrega a referência no valor — tipos primitivos e structs do jogo.</item>
/// <item><c>stfld/stsfld</c> -> <c>call set_X</c> (as ref properties têm
/// setter que escreve no endereço nativo).</item>
/// <item><c>ldflda</c> -> <c>call get_X</c> quando o getter devolve
/// <c>T&amp;</c> (a referência É o endereço — exato para
/// <c>item.position.X = 5f</c>).</item>
/// </list>
///
/// Só são reescritos campos cujo tipo declarante é um proxy CLASSE do shim
/// e cujo nome bate com uma propriedade sem campo de mesmo nome. Todo o
/// resto fica intacto (e é contado no log para diagnosticar a API que falta).
/// </summary>
internal static class IlPatcher
{
    /// <summary>Espaços de nomes do jogo cujos tipos viram proxies.</summary>
    static readonly string[] GameNamespaces = { "Terraria", "ReLogic", "Microsoft.Xna.Framework" };

    static Dictionary<string, Type>? shimMap;
    static MethodInfo? readMethod;

    static Dictionary<string, Type> ShimMap => shimMap ??= BuildMap();

    static Dictionary<string, Type>? structMap;

    /// <summary>
    /// Campos em que o CONTRATO (1.4.4.9) declara <c>List&lt;T&gt;</c> mas o
    /// dump do jogo é ARRAY nativo.
    ///
    /// O mod compilado no PC escreve <c>recipe.acceptedGroups.Contains(id)</c>:
    /// IL = <c>ldflda Recipe::acceptedGroups</c> + <c>callvirt List&lt;int&gt;::Contains</c>.
    /// Reescrevendo o campo para <c>GameArray&lt;int&gt;</c>, a pilha passa a ter
    /// uma STRUCT onde o <c>callvirt</c> espera uma referência de objeto → o JIT
    /// do PC dá AV (0xC0000005, mata o processo) e no aparelho pode virar
    /// SIGSEGV.
    ///
    /// Sem reescrever esses campos o método falha com <c>MissingFieldException</c>
    /// — que é capturável, então o mod continua e só aquela função se perde.
    /// A correção de verdade é expor <c>List&lt;T&gt;</c> de verdade nesses três
    /// campos (ver `共享对话/07-待办清单.md`).
    /// </summary>
    static readonly HashSet<string> ArrayShapeMismatch = new(StringComparer.Ordinal)
    {
        "Terraria.Recipe::acceptedGroups",
        "Terraria.Recipe::requiredItem",
        "Terraria.Recipe::requiredTile",
    };

    /// <summary>
    /// Metodos que o JIT nao consegue compilar e que, por isso, MATAM o
    /// processo (0xC0000005) em vez de lancar uma excecao.
    ///
    /// Diferente de <c>MissingFieldException</c>/<c>MissingMethodException</c>,
    /// que sao capturaveis e deixam o mod seguir, um AV no
    /// <c>PrepareMethod</c> derruba o jogo inteiro na inicializacao. A unica
    /// defesa possivel e NAO deixar o JIT ver esse corpo: trocamos por um
    /// retorno padrao e registramos no log. A funcionalidade daquele metodo
    /// se perde, o resto do mod continua.
    ///
    /// A lista vem do arquivo apontado por <c>BL_QUARANTINE</c> (uma entrada
    /// <c>Tipo::Metodo</c> por linha). O `tools/modsandbox` alimenta esse
    /// arquivo automaticamente: cada AV revela o metodo culpado pelo rastro do
    /// JIT, ele entra na lista e a execucao recomeca.
    /// </summary>
    static HashSet<string>? quarantine;

    static HashSet<string> Quarantine => quarantine ??= LoadQuarantine();

    /// <summary>
    /// Quarentena embutida: métodos que já se sabe que derrubam o JIT. Fica no
    /// código, e não só no arquivo de ambiente, porque no aparelho
    /// <c>BL_QUARANTINE</c> não existe — sem isto o jogo fecharia na
    /// inicialização em vez de só perder a função daquele método.
    ///
    /// Os dois primeiros vêm do Fargowiltas (a causa raiz ainda não foi
    /// isolada; ver `共享对话/07-待办清单.md`, P0-2). Remova daqui quando o
    /// gerador de IL passar a produzir um corpo que o JIT aceite.
    /// </summary>
    /// <summary>
    /// Diagnostico opcional: BL_IL_TRACE=<caminho> faz o patcher gravar, para
    /// cada metodo esvaziado, QUAL membro disparou o `unresolved`. Sem isto o
    /// log so diz "campo sem traducao" e nao ha como saber qual dos 50 calls
    /// do metodo foi o culpado.
    /// </summary>
    static readonly System.IO.StreamWriter? Trace = OpenTrace();

    static System.IO.StreamWriter? OpenTrace()
    {
        string? path = Environment.GetEnvironmentVariable("BL_IL_TRACE");
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var w = new System.IO.StreamWriter(path, append: false) { AutoFlush = true };
            return w;
        }
        catch { return null; }
    }

    static readonly string[] QuarantineDefaults =
    {
        "Fargowiltas.Items.FargoGlobalItem::ModifyTooltips",
        "Fargowiltas.Items.Tiles.MutantToiletSheet::RightClick",
    };

    static HashSet<string> LoadQuarantine()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in QuarantineDefaults) set.Add(Normalize(name));
        string? path = Environment.GetEnvironmentVariable("BL_QUARANTINE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return set;
        foreach (string line in File.ReadAllLines(path))
        {
            string name = line.Trim();
            if (name.Length == 0 || name.StartsWith("#", StringComparison.Ordinal)) continue;
            set.Add(Normalize(name));
        }
        if (set.Count > 0) Log.Info($"il patch: quarentena com {set.Count} metodo(s)");
        return set;
    }

    /// <summary>Cecil usa <c>/</c> e <c>+</c> para tipos aninhados; o rastro usa <c>+</c>.</summary>
    static string Normalize(string name) => name.Replace('/', '+');

    /// <summary>
    /// Structs do shim (Color, Tile, Vector2...). Ficam FORA do
    /// <see cref="ShimMap"/> porque a reescrita de campo precisa de um
    /// receptor <c>IGameObject</c> — mas a troca de chamada por campo
    /// (<see cref="PatchMethodToField"/>) vale para eles.
    /// </summary>
    /// <summary>
    /// Structs do shim (Color, Tile, Vector2...). Ficam FORA do
    /// <see cref="ShimMap"/> porque a reescrita de campo precisa de um
    /// receptor <c>IGameObject</c> — mas a troca de chamada por campo
    /// (<see cref="PatchMethodToField"/>) vale para eles.
    /// </summary>
    static Dictionary<string, Type> StructMap => structMap ??= BuildStructMap();

    static bool IsGameNamespace(string ns) =>
        GameNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));

    static Dictionary<string, Type> BuildStructMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in typeof(IlPatcher).Assembly.GetTypes())
        {
            if (!type.IsValueType || type.IsEnum) continue;
            string ns = type.Namespace ?? "";
            if (!GameNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal))) continue;
            map[type.FullName!] = type;
            string flat = type.FullName!.Replace('/', '.').Replace('+', '.');
            if (!map.ContainsKey(flat)) map[flat] = type;
        }
        return map;
    }

    static Dictionary<string, Type> BuildMap()
    {
        // Custa ~53 s no aparelho (Mono carrega o metadado de milhares de
        // proxies de uma vez) e e pago UMA vez por processo. A tentativa de
        // trocar isto por um indice lido do DLL com Cecil + `Assembly.GetType`
        // sob demanda deixava o assembly reescrito com o mesmo tamanho, mas o
        // JIT estourava 0xC0000005 no sandbox: a semantica do mapa nao era
        // identica. Enquanto nao houver um teste que prove equivalencia, fica
        // a versao por reflexao — cara, porem correta.
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in typeof(IlPatcher).Assembly.GetTypes())
        {
            // Só classes do jogo (static classes são abstract+sealed — entram;
            // classes abstratas de verdade ficam de fora: um mod não faz
            // ldfld num tipo que não pode instanciar... e os proxies do jogo
            // do PC são concretos).
            if (!type.IsClass || type.IsInterface) continue;
            if (type.IsAbstract && !type.IsSealed) continue;
            string ns = type.Namespace ?? "";
            if (!GameNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal))) continue;
            map[type.FullName!] = type;
            string flat = type.FullName!.Replace('/', '.').Replace('+', '.');
            if (!map.ContainsKey(flat)) map[flat] = type;
        }
        return map;
    }

    static MethodInfo ReadMethod => readMethod ??= typeof(IlInterop)
        .GetMethod(nameof(IlInterop.Read), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("IlInterop.Read missing");

    /// <summary>
    /// O IL reescrito do mod chama <see cref="IlInterop.Read{T}"/> — a ponte
    /// pública. Aqui fica só o alias interno, para o resto do loader.
    /// </summary>
    static T Read<T>(ref T value) => IlInterop.Read(ref value);

    /// <summary>Diferença de <see cref="Stopwatch.GetTimestamp"/> em milissegundos.</summary>
    static long Ms(long ticks) => (long)(ticks * 1000.0 / Stopwatch.Frequency);

    /// <summary>
    /// Carrega o assembly do mod, reescrevendo o IL antes de o Mono ver os
    /// bytes. Qualquer falha do Cecil é isolada: devolve o assembly SEM
    /// reescrita para nunca derrubar o carregamento.
    /// </summary>
    internal static Assembly Load(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] patched = Rewrite(path, bytes);
            return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(patched));
        }
        catch (Exception e)
        {
            Log.Warn($"il patch: {Path.GetFileName(path)}: recarrego sem reescrita ({e.GetType().Name}: {e.Message})");
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    static byte[] Rewrite(string path, byte[] bytes)
    {
        // Stopwatch e nao Environment.TickCount64: o shim roda no Mono do
        // aparelho, que nao tem aquele membro (so o .NET moderno tem).
        long readStart = Stopwatch.GetTimestamp();
        var module = ModuleDefinition.ReadModule(new MemoryStream(bytes), new ReaderParameters { ReadSymbols = false });
        long readDone = Stopwatch.GetTimestamp();
        try
        {
        int rewritten = 0, missing = 0, stubbed = 0;
        var absent = new SortedSet<string>(StringComparer.Ordinal);
        var map = ShimMap;
        long mapDone = Stopwatch.GetTimestamp();
        // A reescrita de um mod grande leva dezenas de segundos e nao escreve
        // nada por metodo (so o balanco no fim). Sem um contador o painel fica
        // parado em "正在重写 IL…" e o jogador nao sabe se andou ou travou.
        List<TypeDefinition> types = module.GetTypes().ToList();
        int totalMethods = 0;
        foreach (TypeDefinition t in types) totalMethods += t.Methods.Count;
        // O tempo de abertura de um mod grande se divide entre ler o DLL
        // (Cecil), montar o mapa do shim e o laço em si. Sem esta linha, um
        // minuto parado no painel não diz em qual dos três ele foi gasto.
        Log.Info($"il patch: {Path.GetFileName(path)}: {types.Count} tipos, {totalMethods} métodos "
            + $"(ler {Ms(readDone - readStart)} ms, mapa {Ms(mapDone - readDone)} ms)");
        int doneMethods = 0;
        long lastBeat = Stopwatch.GetTimestamp();
        foreach (TypeDefinition type in types)
            foreach (MethodDefinition method in type.Methods)
            {
                // Um método problemático não pode derrubar a reescrita do
                // arquivo inteiro (o catch externo devolve o assembly SEM
                // patch, e aí TODO acesso de campo quebra em execução).
                try
                {
                    PatchMethod(module, method, map, ref rewritten, ref missing, ref stubbed, absent);
                }
                catch (Exception e)
                {
                    Log.Warn($"il patch: {type.FullName}.{method.Name}: pulado ({e.GetType().Name}: {e.Message})");
                }
                finally
                {
                    doneMethods++;
                    // O relógio manda, não a contagem: um método com muitos
                    // acessos de campo custa centenas de ms, e 256 métodos sem
                    // batida deixavam o painel parado por minutos — foi assim
                    // que o Luminance ficou 66 s em "正在重写 IL：Luminance…".
                    // Duas batidas por segundo é o que a tela consegue mostrar.
                    long now = Stopwatch.GetTimestamp();
                    if (Ms(now - lastBeat) >= 500 || (doneMethods & 0x3F) == 0)
                    {
                        lastBeat = now;
                        Progress.ModStep($"正在重写 IL：{doneMethods}/{totalMethods} 个方法，已改 {rewritten} 处字段…",
                            0.25 + 0.17 * doneMethods / (double)Math.Max(1, totalMethods));
                    }
                }
            }
        if (rewritten == 0 && missing == 0 && stubbed == 0)
        {
            module.Dispose();
            return bytes;
        }
        Log.Info($"il patch: {Path.GetFileName(path)}: {rewritten} acesso(s) de campo reescrito(s), {missing} sem tradução, {stubbed} corpo(s) esvaziado(s)");
        foreach (string name in absent)
            Log.Info($"il patch:   campo sem propriedade no shim: {name}");
        if (rewritten == 0 && stubbed == 0)
            {
                module.Dispose();
                return bytes;
            }
            var output = new MemoryStream();
            module.Write(output, new WriterParameters { WriteSymbols = false });
            // Diagnostico: BL_IL_OUT=<pasta> grava o assembly JA REESCRITO.
            // Sem isto, um InvalidProgramException no corpo gerado so diz o
            // offset, sem dar como ver o que o patcher escreveu ali.
            string? dumpDir = Environment.GetEnvironmentVariable("BL_IL_OUT");
            if (!string.IsNullOrEmpty(dumpDir))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dumpDir);
                    System.IO.File.WriteAllBytes(
                        System.IO.Path.Combine(dumpDir, System.IO.Path.GetFileName(path)),
                        output.ToArray());
                }
                catch (Exception e) { Log.Warn($"il patch: BL_IL_OUT: {e.Message}"); }
            }
            module.Dispose();
            return output.ToArray();
        }
        catch
        {
            module.Dispose();
            throw;
        }
    }

    static void PatchMethod(ModuleDefinition module, MethodDefinition method,
        Dictionary<string, Type> map, ref int rewritten, ref int missing, ref int stubbed, SortedSet<string> absent)
    {
        if (!method.HasBody) return;
        if (Quarantine.Count > 0 &&
            Quarantine.Contains(Normalize($"{method.DeclaringType.FullName}::{method.Name}")))
        {
            Stub(method);
            stubbed++;
            return;
        }
        var body = method.Body;
        var il = body.GetILProcessor();
        int startRewritten = rewritten;
        // Acesso que NAO pode ser traduzido deixa a instrucao original
        // apontando para um membro que o shim nao tem. O JIT do Mono recebe
        // uma referencia quebrada e, ao resolve-la, cai dentro de
        // libmonosgen (PC +0xC8758, si_addr 0) — sem excecao gerenciada e
        // sem uma linha de log. Melhor entregar um metodo vazio.
        bool unresolved = false;
        var reasons = new List<string>();
        // Instrucao que deixa um "array do jogo" na pilha -> como ele e
        // acessado. Serve para consertar `ldelem/stelem/ldlen` depois
        // (ver PatchArrayElements).
        var arrays = new Dictionary<Instruction, ArrayRef>();
        foreach (Instruction ins in body.Instructions.ToArray())
        {
            // `get_X()`/`set_X()` chamados como MÉTODO mas que no shim são
            // CAMPO: acontece nas structs onde o FNA expõe propriedade e o
            // dump campo (Color.R/G/B/A, por exemplo). Sem isto o mod não JITa.
            if (ins.Operand is MethodReference called && ins.OpCode.Code is Code.Call or Code.Callvirt)
            {
                int before = rewritten;
                PatchMethodToField(module, ins, called, map, ref rewritten);
                // Método do jogo que o shim não tem (1.4.5 deletou): o `call`
                // fica apontando para membro inexistente e o JIT do Mono cai
                // no mesmo libmonosgen +0xC8758 que o campo. Se o
                // PatchMethodToField não converteu (get_/set_ que continuam
                // sendo propriedade de verdade no shim), a assinatura ainda
                // precisa bater — `TeleportPylonsSystem.get_Pylons()` devolve
                // tipo diferente e também derruba o JIT.
                if (rewritten == before && IsMissingGameMethod(called))
                { unresolved = true; reasons.Add($"metodo ausente {called.DeclaringType.FullName}::{called.Name}({called.Parameters.Count})"); }
                continue;
            }
            if (ins.Operand is not FieldReference field) continue;
            switch (ins.OpCode.Code)
            {
                case Code.Ldfld or Code.Ldflda or Code.Ldsflda or Code.Stfld or Code.Ldsfld or Code.Stsfld:
                    break;
                default:
                    continue;
            }
            string? owner = OwnerOf(field.DeclaringType);
            if (owner == null) continue;
            if (!map.TryGetValue(owner, out Type shim))
            {
                // Struct proxy (PlayerDrawSet, DrawData, ...): a pilha tem o
                // ENDERECO do struct original, entao `ldobj` vira o valor do
                // proxy e o getter estatico do shim resolve o campo pela
                // classe real. Antes isto ficava sem reescrita e virava
                // MissingField em execucao — o `PlayerDrawSet.drawPlayer`
                // derrubava `FargoPlayerBuffDrawLayer.GetDefaultVisibility`.
                if (StructMap.TryGetValue(owner, out Type structShim))
                {
                    if (TryRewriteStructField(module, il, ins, field, structShim))
                    {
                        rewritten++;
                        continue;
                    }
                    if (!HasRealField(structShim, field.Name))
                    {
                        absent.Add($"{owner}::{field.Name}");
                        unresolved = true;
                        reasons.Add($"sem propriedade {owner}::{field.Name}");
                    }
                    continue;
                }
                // 1.4.5 removeu o tipo inteiro: referencia quebrada derruba o
                // JIT do Mono, entao o metodo vai vazio.
                if (IsGameType(owner))
                { unresolved = true; reasons.Add($"tipo ausente {owner}::{field.Name}"); }
                continue;
            }
            if (ArrayShapeMismatch.Contains($"{owner}::{field.Name}")) continue;
            PropertyInfo? property = FindProperty(shim, field.Name);
            if (property == null)
            {
                // Nem propriedade nem campo real: o runtime vai recusar.
                // Registrar mostra exatamente qual API do PC falta portar.
                if (!HasRealField(shim, field.Name))
                {
                    absent.Add($"{owner}::{field.Name}");
                    unresolved = true; reasons.Add($"sem propriedade {owner}::{field.Name}");
                }
                continue;
            }
            // Os acessadores que o ProxyGen emite são STATIC e recebem
            // IGameObject: no shim cada proxy é uma classe SOLTA (Item não
            // herda Entity), então `callvirt Entity::__set_width` num Item
            // estourava NullReference e o SetDefaults do mod parava ali —
            // todos os campos seguintes ficavam no default.
            MethodInfo? refGetter = property.GetGetMethod(true);
            MethodInfo? plainGetter = FindMethod(shim, "__get_" + field.Name);
            MethodInfo? byRefGetter = FindMethod(shim, "__ref_" + field.Name);
            MethodInfo? setter = FindMethod(shim, "__set_" + field.Name);

            // Campos ESTÁTICOS: o ProxyGen gera `__get_X` / `__set_X` PRIVATE e
            // os embrulha na propriedade pública `X`. Apontar o mod para o
            // private dá MethodAccessException (v35: StatSheetUI.OnInitialize
            // chamava `Main.__get_screenWidth()`). Nesses casos usa o accessor
            // público da propriedade, que tem a MESMA assinatura. Campo de
            // INSTÂNCIA tem `__get_X(IGameObject)` public static — esse segue.
            if (plainGetter is not null && !plainGetter.IsPublic) plainGetter = null;
            if (byRefGetter is not null && !byRefGetter.IsPublic) byRefGetter = null;
            if (setter is not null && !setter.IsPublic) setter = null;
            setter ??= property.GetSetMethod(true);

            switch (ins.OpCode.Code)
            {
                case Code.Ldfld or Code.Ldsfld:
                    MethodInfo? getter = byRefGetter ?? plainGetter ?? refGetter;
                    if (getter == null) { missing++; if (!HasRealField(shim, field.Name)) { unresolved = true; reasons.Add($"sem getter {owner}::{field.Name}"); } continue; }
                    ins.OpCode = getter.IsStatic ? OpCodes.Call : OpCodes.Callvirt;
                    ins.Operand = module.ImportReference(getter);
                    rewritten++;
                    if (getter.ReturnType.IsByRef)
                    {
                        Type element = getter.ReturnType.GetElementType()!;
                        if (IsInlineArray(element))
                        {
                            // Array EMBUTIDO (Projectile.ai, Item.glowColor…):
                            // o elemento vive DENTRO do dono, então `stelem`
                            // precisa do ENDEREÇO — uma cópia por valor
                            // escreveria no nada. Deixamos o `T&` do getter na
                            // pilha e só descarregamos em valor se o array for
                            // usado inteiro (ver Materialize em
                            // PatchArrayElements).
                            TrackArray(ins, element, byRef: true, arrays);
                        }
                        else
                        {
                            // O valor em si interessa (GameArray/GameRefArray):
                            // descarrega a referência e marca a instrução que
                            // REALMENTE empilha o array — o `Read<T>` —, não o
                            // getter. Marcar o getter fazia a busca pelo
                            // produtor errar e o `ldelem` ficar sobre a struct.
                            var generic = new GenericInstanceMethod((MethodReference)module.ImportReference(ReadMethod));
                            generic.GenericArguments.Add(module.ImportReference(element));
                            Instruction read = il.Create(OpCodes.Call, generic);
                            il.InsertAfter(ins, read);
                            TrackArray(read, element, byRef: false, arrays);
                        }
                    }
                    break;

                case Code.Stfld or Code.Stsfld:
                    if (setter == null) { missing++; if (!HasRealField(shim, field.Name)) { unresolved = true; reasons.Add($"sem setter {owner}::{field.Name}"); } continue; }
                    ins.OpCode = setter.IsStatic ? OpCodes.Call : OpCodes.Callvirt;
                    ins.Operand = module.ImportReference(setter);
                    rewritten++;
                    break;

                case Code.Ldflda or Code.Ldsflda:
                    // O endereço do campo: __ref_X devolve T&, que É o
                    // endereço nativo. `ldsflda` (estático) vinha caindo fora
                    // do switch acima e ficava intacto -> o mod passava `ref
                    // Main.drunkWorld` para um método e o shim (que expõe isso
                    // como PROPRIEDADE ref-return) não tinha campo -> MissingField.
                    MethodInfo? address = byRefGetter != null
                        ? byRefGetter
                        : (refGetter != null && refGetter.ReturnType.IsByRef ? refGetter : null);
                    if (address != null)
                    {
                        ins.OpCode = address.IsStatic ? OpCodes.Call : OpCodes.Callvirt;
                        ins.Operand = module.ImportReference(address);
                        rewritten++;
                        // `ldflda` deixa o ENDERECO na pilha: se for array
                        // embutido, o `ldelem` seguinte já está servido.
                        if (address.ReturnType.IsByRef)
                        {
                            Type element = address.ReturnType.GetElementType()!;
                            if (IsInlineArray(element)) TrackArray(ins, element, byRef: true, arrays);
                        }
                    }
                    else { missing++; if (!HasRealField(shim, field.Name)) { unresolved = true; reasons.Add($"sem endereco {owner}::{field.Name}"); } }
                    break;
            }
        }

        if (unresolved)
        {
            // O corpo ainda tem instrucao apontando para membro inexistente.
            // Isto e o que derrubava o processo no JIT do Mono, entao o
            // metodo vai vazio em vez de invalido.
            Stub(method);
            stubbed++;
            Log.Warn($"il patch: {method.DeclaringType.FullName}.{method.Name}: campo sem traducao; corpo trocado por retorno padrao");
            if (Trace != null)
                Trace.WriteLine($"{method.FullName} :: {string.Join(", ", reasons)}");
            return;
        }

        PatchArrayElements(module, body, arrays);

        // O corpo CRESCEU: cada `ldfld` (3 bytes) virou `call` (5) e cada
        // getter byref ganhou mais um `call Read<T>`. O Cecil NAO alarga
        // `br.s`/`brtrue.s` sozinho — ele recalcula o destino mas grava o
        // delta num sbyte, e quando a distancia passa de 127 o valor estoura.
        // O arquivo sai com um desvio para fora do metodo: relendo, o alvo
        // vira `null`, e o JIT do Mono responde `InvalidProgramException`
        // (eram 69 metodos do Fargowiltas, sempre os que tem `if` + campo).
        if (rewritten != startRewritten) WidenBranches(body);
    }

    /// <summary>
    /// Troca o corpo por um "retorno padrao". Usado so na quarentena (ver
    /// <see cref="Quarantine"/>): o objetivo e tirar do JIT um IL que ele nao
    /// consegue compilar sem derrubar o processo. Nao preserva efeito nenhum.
    /// </summary>
    static void Stub(MethodDefinition method)
    {
        try
        {
            var body = method.Body;
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();
            body.Variables.Clear();
            var il = body.GetILProcessor();

            // Devolver `ref` exigiria um endereco valido: nao ha o que
            // devolver com seguranca, entao esse caso fica como esta.
            if (method.ReturnType.IsByReference)
            {
                Log.Warn($"il patch: {method.FullName}: quarentena ignorada (retorna byref)");
                return;
            }

            if (method.ReturnType.MetadataType != MetadataType.Void)
            {
                TypeReference ret = method.Module.ImportReference(method.ReturnType);
                var local = new VariableDefinition(ret);
                body.Variables.Add(local);
                body.InitLocals = true;
                il.Append(il.Create(OpCodes.Ldloca, local));
                il.Append(il.Create(OpCodes.Initobj, ret));
                il.Append(il.Create(OpCodes.Ldloc, local));
            }
            il.Append(il.Create(OpCodes.Ret));
            body.MaxStackSize = 8;
            Log.Warn($"il patch: {method.FullName}: em quarentena (corpo trocado por retorno padrao)");
        }
        catch (Exception e)
        {
            Log.Warn($"il patch: {method.FullName}: quarentena falhou ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>
    /// Alarga os desvios curtos que passaram a estourar.
    ///
    /// <c>br.s</c>/<c>brtrue.s</c> guardam o destino num <b>sbyte</b>. O Cecil
    /// NAO reescreve a forma curta para a longa quando o corpo cresce: ele
    /// grava o delta truncado, o desvio cai fora do metodo e — relendo o
    /// arquivo — o alvo vira <c>null</c>, que o JIT traduz em
    /// <c>InvalidProgramException</c>. Era o caso de 69 metodos do
    /// Fargowiltas (todos com <c>if</c> + acesso a campo).
    ///
    /// Alarga em passes: cada alargamento aumenta o corpo e pode empurrar
    /// outro desvio vizinho para fora da faixa, entao recalcula os offsets
    /// ate estabilizar. So toca no que realmente estourou — alargar tudo
    /// deixava o corpo maior do que o necessario.
    ///
    /// <c>BL_WIDEN=off</c> desliga (comparacao A/B), <c>=all</c> alarga tudo.
    /// </summary>
    static void WidenBranches(Mono.Cecil.Cil.MethodBody body)
    {
        string mode = Environment.GetEnvironmentVariable("BL_WIDEN") ?? "overflow";
        if (mode == "off") return;

        for (int pass = 0; pass < 16; pass++)
        {
            var at = new Dictionary<Instruction, int>(body.Instructions.Count);
            int offset = 0;
            foreach (Instruction i in body.Instructions)
            {
                at[i] = offset;
                offset += i.GetSize();
            }

            bool changed = false;
            foreach (Instruction i in body.Instructions)
            {
                if (i.Operand is not Instruction target) continue;
                if (!ShortToLong.TryGetValue(i.OpCode.Code, out OpCode wide)) continue;
                if (mode != "all")
                {
                    long delta = (long)at[target] - at[i] - 1;
                    if (delta >= sbyte.MinValue && delta <= sbyte.MaxValue) continue;
                }
                i.OpCode = wide;
                changed = true;
            }
            if (!changed) return;
        }
    }

    static readonly Dictionary<Code, OpCode> ShortToLong = new()
    {
        [Code.Br_S] = OpCodes.Br,
        [Code.Brfalse_S] = OpCodes.Brfalse,
        [Code.Brtrue_S] = OpCodes.Brtrue,
        [Code.Beq_S] = OpCodes.Beq,
        [Code.Bge_S] = OpCodes.Bge,
        [Code.Bgt_S] = OpCodes.Bgt,
        [Code.Ble_S] = OpCodes.Ble,
        [Code.Blt_S] = OpCodes.Blt,
        [Code.Bne_Un_S] = OpCodes.Bne_Un,
        [Code.Bge_Un_S] = OpCodes.Bge_Un,
        [Code.Bgt_Un_S] = OpCodes.Bgt_Un,
        [Code.Ble_Un_S] = OpCodes.Ble_Un,
        [Code.Blt_Un_S] = OpCodes.Blt_Un,
        [Code.Leave_S] = OpCodes.Leave,
    };

    /// <summary>
    /// <c>call get_G()</c> -> <c>ldfld Color::G</c>.
    ///
    /// O FNA do PC declara alguns membros como PROPRIEDADE e este dump os
    /// declara como CAMPO (é o caso de <c>Color.R/G/B/A</c>). O proxy então
    /// não tem <c>get_G()</c> e o método do mod não JITa. Aqui a chamada é
    /// desfeita no campo equivalente — a pilha fica com a mesma forma
    /// (receptor entra, valor sai).
    /// </summary>
    static void PatchMethodToField(ModuleDefinition module, Instruction ins, MethodReference method,
        Dictionary<string, Type> map, ref int rewritten)
    {
        string name = method.Name;
        bool getter = name.StartsWith("get_", StringComparison.Ordinal);
        bool setter = name.StartsWith("set_", StringComparison.Ordinal);
        if (!getter && !setter) return;
        if (method.HasGenericParameters) return;
        if (setter && method.Parameters.Count != 1) return;
        if (getter && method.Parameters.Count != 0) return;
        if (method.DeclaringType is not TypeReference declaring) return;
        if (declaring is GenericInstanceType) return;
        string? owner = OwnerOf(declaring);
        if (owner == null) return;
        if (!map.TryGetValue(owner, out Type shim) && !StructMap.TryGetValue(owner, out shim)) return;

        string fieldName = name[4..];
        if (FindMethod(shim, name) != null) return; // existe de verdade: não mexe
        FieldInfo? target = FindField(shim, fieldName);
        if (target == null) return;

        // O campo tem de ter o MESMO tipo que a assinatura do método: emitir
        // `ldfld` com tipo trocado gera IL inválido (crash no aparelho, pior
        // que o MissingMethod original).
        string expected = (setter ? method.Parameters[0].ParameterType : method.ReturnType).FullName;
        if (!string.Equals(target.FieldType.FullName, expected, StringComparison.Ordinal)) return;

        bool isStatic = target.IsStatic;
        TypeReference fieldType = module.ImportReference(setter ? method.Parameters[0].ParameterType : method.ReturnType);
        ins.OpCode = getter
            ? (isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld)
            : (isStatic ? OpCodes.Stsfld : OpCodes.Stfld);
        ins.Operand = new FieldReference(fieldName, fieldType, module.ImportReference(declaring));
        rewritten++;
    }


    /// <summary>
    /// O que está na pilha quando o produtor roda: o tipo CLR fechado do array
    /// e se está lá o VALOR (<c>GameArray&lt;T&gt;</c>) ou o ENDEREÇO
    /// (<c>T&lt;ArrayEmbutido&gt;&amp;</c>).
    /// </summary>
    readonly struct ArrayRef
    {
        internal readonly Type Clr;
        internal readonly bool ByRef;

        internal ArrayRef(Type clr, bool byRef) { Clr = clr; ByRef = byRef; }
    }

    /// <summary>
    /// Array EMBUTIDO no dono: o dump do il2cpp declara <c>Projectile.ai</c> e
    /// <c>Item.glowColor</c> como struct de tamanho fixo, não como <c>T[]</c>.
    /// Escrever num array desses exige o endereço do campo — uma cópia por
    /// valor descarta o valor em silêncio.
    /// </summary>
    static bool IsInlineArray(Type type)
    {
        if (!type.IsValueType || type.IsEnum || type.IsPrimitive) return false;
        if (type.IsGenericType)
        {
            string generic = type.GetGenericTypeDefinition().Name;
            return generic is not ("GameArray`1" or "GameRefArray`1");
        }
        return true;
    }

    /// <summary>
    /// O próximo opcode consome um elemento do array? Decide se o getter
    /// byref deve manter o <c>T&amp;</c> (acesso a elemento) ou pode ser
    /// descarregado em valor (<c>Read&lt;T&gt;</c>, uso do array inteiro).
    /// </summary>
    static bool NextIsElementAccess(Instruction ins)
    {
        if (ins.Next == null) return false;
        return IsLdelem(ins.Next.OpCode.Code) || IsStelem(ins.Next.OpCode.Code) ||
               ins.Next.OpCode.Code is Code.Ldlen or Code.Ldelema;
    }

    static void TrackArray(Instruction ins, Type clrType, bool byRef, Dictionary<Instruction, ArrayRef> arrays)
    {
        if (clrType.IsByRef) clrType = clrType.GetElementType()!;
        if (!byRef && !clrType.IsGenericType) return;
        arrays[ins] = new ArrayRef(clrType, byRef);
    }


    /// <summary>
    /// Conserta o acesso a ELEMENTO de array do jogo.
    ///
    /// Um mod compilado no PC escreve <c>Main.debuff[Type] = true</c> como
    /// <c>ldsfld Main::debuff; ...; stelem.i1</c>. Depois da reescrita de
    /// campo, o que está na pilha é a STRUCT <c>GameArray&lt;bool&gt;</c> — e
    /// <c>stelem.i1</c> só aceita array gerenciado, então o Mono aborta com
    /// <c>InvalidProgramException</c> (era o que derrubava os 22 buffs do
    /// Fargowiltas, sempre em <c>SetStaticDefaults</c>).
    ///
    /// A tradução é a mesma ideia do campo: <c>ldelem</c> -> <c>Get(index)</c>,
    /// <c>stelem</c> -> <c>Set(index, value)</c>, <c>ldlen</c> ->
    /// <c>get_Length</c>. Para array EMBUTIDO o alvo é
    /// <c>IlInterop.ArrayGet/ArraySet</c>, que recebem o endereço.
    ///
    /// Para saber QUAL instrução empilhou o array, simulamos a pilha de
    /// avaliação guardando o PRODUTOR de cada valor — não a altura.
    ///
    /// A altura não serve: <c>ldarg.0; call get_Projectile; call __ref_ai;
    /// call Read&lt;T&gt;</c> empilha e desempilha na MESMA posição, então
    /// procurar "quem empilhou no slot 0" apontava para o <c>ldarg.0</c> lá
    /// atrás, o <c>ldelem.r4</c> de <c>Projectile.ai[0]</c> nunca era
    /// reescrito e o JIT matava o método (41 dos 69 InvalidProgram do
    /// Fargowiltas eram exatamente isso: <c>get_HomingCooldown</c>,
    /// <c>SetDefaults</c> dos NPCs, <c>AI</c> dos projéteis).
    ///
    /// Alvos de desvio reiniciam a pilha: a verificação do CLR exige pilha
    /// vazia na entrada de bloco, então ali o rastreio recomeça do zero.
    /// </summary>
    static void PatchArrayElements(ModuleDefinition module, Mono.Cecil.Cil.MethodBody body,
        Dictionary<Instruction, ArrayRef> arrays)
    {
        if (arrays.Count == 0) return;

        Instruction[] ins = body.Instructions.ToArray();

        // Alvos de desvio: entrada de bloco básico tem pilha vazia.
        var targets = new HashSet<Instruction>();
        foreach (Instruction i in ins)
        {
            if (i.Operand is Instruction target) targets.Add(target);
            else if (i.Operand is Instruction[] many) foreach (Instruction t in many) targets.Add(t);
        }

        // Um opcode fora da nossa tabela invalida a simulação: nesse caso não
        // reescrevemos NADA (deixar o MissingField/InvalidProgram original é
        // melhor que reescrever no lugar errado). Valida antes de mexer.
        foreach (Instruction i in ins)
        {
            Code code = i.OpCode.Code;
            if (code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                if (i.Operand is not MethodReference) return;
                continue;
            }
            if (code == Code.Dup) continue;
            if (Delta(i) is null) return;
        }

        var il = body.GetILProcessor();

        // Pilha de avaliação: para cada posição, a instrução que PRODUZIU o
        // valor. `null` = valor de origem desconhecida (local, argumento…).
        var stack = new List<Instruction?>();
        // produtor -> instrucao `Read<T>` inserida por ele (array por endereco)
        var materialized = new Dictionary<Instruction, Instruction>();
        var boxed = new HashSet<Instruction>();
        // Profundidade da pilha em cada alvo de desvio (vista no desvio). O
        // `x ?? (x = new ...)` que o Roslyn gera para cache de delegate deixa
        // o ARRAY na pilha ATRAVES do desvio; limpar a pilha ao chegar no alvo
        // perdia o array e o `box` nunca era inserido (FargoWorld.NoBosses e
        // mais 20 metodos seguiam InvalidProgram).
        var branchDepth = new Dictionary<Instruction, int>();

        foreach (Instruction current in ins)
        {
            if (targets.Contains(current))
            {
                int depth = stack.Count;
                if (!branchDepth.TryGetValue(current, out int seen) || seen != depth) stack.Clear();
            }

            int operands = current.OpCode.Code switch
            {
                Code.Ldlen => 0,
                Code.Ldelem_Any or Code.Ldelem_I1 or Code.Ldelem_U1 or Code.Ldelem_I2 or Code.Ldelem_U2
                    or Code.Ldelem_I4 or Code.Ldelem_U4 or Code.Ldelem_I8 or Code.Ldelem_R4 or Code.Ldelem_R8
                    or Code.Ldelem_Ref => 1,
                Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1 or Code.Stelem_I2 or Code.Stelem_I4
                    or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8 or Code.Stelem_Ref => 2,
                _ => -1,
            };
            int arraySlot = -1;
            if (operands >= 0)
            {
                int at = stack.Count - 1 - operands;
                if (at >= 0 && stack[at] is Instruction producer &&
                    arrays.TryGetValue(producer, out ArrayRef array) &&
                    RewriteElement(module, il, current, array))
                    arraySlot = at;
            }

            // Efeito na pilha, já com o opcode novo (os deltas são os mesmos:
            // `ldelem` -> `call Get`, `stelem` -> `call Set`, `ldlen` ->
            // `call GetLength`).
            Code now = current.OpCode.Code;
            if (now is Code.Call or Code.Callvirt or Code.Newobj)
            {
                var callee = (MethodReference)current.Operand;
                int pops = now == Code.Newobj
                    ? callee.Parameters.Count
                    : callee.Parameters.Count + (callee.HasThis ? 1 : 0);
                int pushes = now == Code.Newobj || callee.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
                for (int p = 0; p < pops; p++)
                {
                    int slot = stack.Count - 1;
                    if (slot >= 0 && slot != arraySlot)
                    {
                        Materialize(module, il, stack[slot], arrays, materialized);
                        // Array do jogo entregue a um parametro de REFERENCIA
                        // (IEnumerable<T> em Enumerable.All/Any/Where, object,
                        // IList<T>...) ou como RECEPTOR de um membro de
                        // interface (foreach -> IEnumerable<T>.GetEnumerator):
                        // a struct tem de ir BOXED, senao o JIT recusa o corpo
                        // inteiro com InvalidProgram. Era o que apagava
                        // FargoWorld.NoBosses, ProcessTriggers, KillTile e
                        // mais 24 metodos.
                        TypeReference? target = ParamAt(callee, now, p)
                            ?? (now != Code.Newobj && callee.HasThis && p == pops - 1 ? callee.DeclaringType : null);
                        BoxIfReference(module, il, stack[slot], target, arrays, materialized, boxed);
                    }
                    Pop(stack);
                }
                for (int p = 0; p < pushes; p++) stack.Add(current);
            }
            else if (now == Code.Dup)
            {
                stack.Add(stack.Count > 0 ? stack[^1] : null);
            }
            else
            {
                // Array do jogo indo para um LOCAL/ARG de interface ou sendo
                // devolvido por um metodo que promete IEnumerable<T>/object:
                // mesmo caso do parametro, precisa de box.
                TypeReference? target = current.OpCode.Code switch
                {
                    Code.Ret => body.Method.ReturnType,
                    Code.Stloc => (current.Operand as VariableDefinition)?.VariableType,
                    Code.Starg => (current.Operand as ParameterDefinition)?.ParameterType,                    Code.Stloc_0 => body.Variables.Count > 0 ? body.Variables[0].VariableType : null,
                    Code.Stloc_1 => body.Variables.Count > 1 ? body.Variables[1].VariableType : null,
                    Code.Stloc_2 => body.Variables.Count > 2 ? body.Variables[2].VariableType : null,
                    Code.Stloc_3 => body.Variables.Count > 3 ? body.Variables[3].VariableType : null,
                    _ => null,
                };
                if (target != null && stack.Count > 0)
                    BoxIfReference(module, il, stack[^1], target, arrays, materialized, boxed);
                int delta = Delta(current) ?? 0;
                for (int p = 0; p < delta; p++) stack.Add(current);
                for (int p = 0; p < -delta; p++)
                {
                    int slot = stack.Count - 1;
                    if (slot >= 0) Materialize(module, il, stack[slot], arrays, materialized);
                    Pop(stack);
                }
                // Desvio: guarda como a pilha chega no alvo.
                if (current.Operand is Instruction jump) branchDepth[jump] = stack.Count;
                else if (current.Operand is Instruction[] jumps)
                    foreach (Instruction jump2 in jumps) branchDepth[jump2] = stack.Count;
            }
        }
    }

    /// <summary>
    /// O array embutido ficou na pilha como ENDEREÇO (<c>T&amp;</c>). Se quem
    /// vai consumi-lo não é um acesso a elemento, o valor é que interessa:
    /// insere o <c>Read&lt;T&gt;</c> logo depois do getter. Uma vez só por
    /// getter — cada <c>__ref_X()</c> empilha um endereço novo.
    /// </summary>
    static void Materialize(ModuleDefinition module, ILProcessor il, Instruction? producer,
        Dictionary<Instruction, ArrayRef> arrays, Dictionary<Instruction, Instruction> done)
    {
        if (producer == null) return;
        if (!arrays.TryGetValue(producer, out ArrayRef info) || !info.ByRef) return;
        if (done.ContainsKey(producer)) return;
        var generic = new GenericInstanceMethod((MethodReference)module.ImportReference(ReadMethod));
        generic.GenericArguments.Add(module.ImportReference(info.Clr));
        Instruction read = il.Create(OpCodes.Call, generic);
        il.InsertAfter(producer, read);
        done[producer] = read;
    }

    static void Pop(List<Instruction?> stack)
    {
        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// Parametro que corresponde ao argumento <paramref name="popped"/> (0 é o
    /// ULTIMO da assinatura, que é o primeiro a sair da pilha). Devolve
    /// <c>null</c> para o <c>this</c> de chamada de instância.
    /// </summary>
    static TypeReference? ParamAt(MethodReference callee, Code code, int popped)
    {
        int count = callee.Parameters.Count;
        int index = count - 1 - popped;
        return index >= 0 && index < count ? callee.Parameters[index].ParameterType : null;
    }

    /// <summary>
    /// Parametros de REFERENCIA que recebem array do jogo. A struct
    /// (<c>GameArray&lt;T&gt;/GameRefArray&lt;T&gt;</c>) implementa
    /// <c>IEnumerable&lt;T&gt;</c>, mas o IL precisa do <c>box</c> para
    /// satisfazer um tipo de referencia — sem ele o JIT recusa o corpo todo.
    /// A lista é fechada DE PROPOSITO: box num parametro que já é a própria
    /// struct produziria o mesmo InvalidProgram.
    /// </summary>
    static bool IsReferenceParam(TypeReference param)
    {
        if (param is ByReferenceType or PointerType or ArrayType or GenericParameter) return false;
        string name = (param.Namespace is { Length: > 0 } ns ? ns + "." : "") + param.Name;
        return name is "System.Collections.Generic.IEnumerable`1"
            or "System.Collections.Generic.IList`1"
            or "System.Collections.Generic.IReadOnlyList`1"
            or "System.Collections.Generic.ICollection`1"
            or "System.Collections.Generic.IReadOnlyCollection`1"
            or "System.Collections.IEnumerable"
            or "System.Object"
            or "System.Array";
    }

    static void BoxIfReference(ModuleDefinition module, ILProcessor il, Instruction? producer,
        TypeReference? target, Dictionary<Instruction, ArrayRef> arrays,
        Dictionary<Instruction, Instruction> materialized, HashSet<Instruction> done)
    {
        if (producer == null || target == null) return;
        if (!arrays.TryGetValue(producer, out ArrayRef info)) return;
        if (!IsReferenceParam(target)) return;
        if (!done.Add(producer)) return;
        TypeReference value = module.ImportReference(info.Clr);
        // O box vai DEPOIS do `Read<T>` quando o valor saiu por endereco:
        // antes dele quem entrava na caixa era o endereço.
        Instruction anchor = materialized.TryGetValue(producer, out Instruction? read) ? read : producer;
        il.InsertAfter(anchor, il.Create(OpCodes.Box, value));
    }

    /// <summary>
    /// Troca <c>ldelem/stelem/ldlen</c> pelo acessador equivalente.
    /// </summary>
    static bool RewriteElement(ModuleDefinition module, ILProcessor il, Instruction current, ArrayRef array)
    {
        Code code = current.OpCode.Code;

        if (array.ByRef)
        {
            // Array EMBUTIDO: a pilha tem `TArray&`. `ArrayGet/ArraySet`
            // recebem exatamente isso, então o elemento é lido/escrito no
            // lugar certo da memória do dono.
            if (code == Code.Ldlen)
            {
                // O tamanho de um array embutido é constante ou um campo —
                // não dá para ler genericamente. Consome o endereço e deixa
                // o valor literal quando ele é conhecido.
                FieldInfo? length = array.Clr.GetField("Length",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (length is not { IsLiteral: true } || length.GetRawConstantValue() is not int size) return false;
                current.OpCode = OpCodes.Nop;
                current.Operand = null;
                il.InsertAfter(current, il.Create(OpCodes.Pop));
                il.InsertAfter(current.Next!, il.Create(OpCodes.Ldc_I4, size));
                return true;
            }

            Type? element = ElementOf(code, current.Operand);
            if (element == null) return false;
            MethodInfo? target = ElementAccessor(IsLdelem(code) ? "ArrayGet" : "ArraySet");
            if (target == null) return false;

            var generic = new GenericInstanceMethod((MethodReference)module.ImportReference(target));
            generic.GenericArguments.Add(module.ImportReference(array.Clr));
            generic.GenericArguments.Add(module.ImportReference(element));

            if (IsStelem(code)) InsertConv(il, current, element, beforeConv: true);
            current.OpCode = OpCodes.Call;
            current.Operand = generic;
            if (IsLdelem(code)) InsertConv(il, current, element, beforeConv: false);
            return true;
        }

        Type clr = array.Clr;
        Type elem = clr.GetGenericArguments()[0];
        if (code == Code.Ldlen)
        {
            MethodInfo? length = clr.GetMethod("GetLength", new[] { clr });
            if (length == null) return false;
            current.OpCode = OpCodes.Call;
            current.Operand = module.ImportReference(length);
            return true;
        }

        if (IsLdelem(code))
        {
            MethodInfo? get = clr.GetMethod("Get", new[] { clr, typeof(int) });
            if (get == null) return false;
            current.OpCode = OpCodes.Call;
            current.Operand = module.ImportReference(get);
            InsertConv(il, current, elem, beforeConv: false);
            return true;
        }
        else
        {
            MethodInfo? set = clr.GetMethod("Set", new[] { clr, typeof(int), elem });
            if (set == null) return false;
            InsertConv(il, current, elem, beforeConv: true);
            current.OpCode = OpCodes.Call;
            current.Operand = module.ImportReference(set);
            return true;
        }
    }

    static MethodInfo? ElementAccessor(string name)
    {
        foreach (MethodInfo candidate in typeof(IlInterop).GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (candidate.Name == name && candidate.IsGenericMethodDefinition &&
                candidate.GetGenericArguments().Length == 2) return candidate;
        return null;
    }

    static bool IsLdelem(Code code) => code is Code.Ldelem_Any or Code.Ldelem_I1 or Code.Ldelem_U1
        or Code.Ldelem_I2 or Code.Ldelem_U2 or Code.Ldelem_I4 or Code.Ldelem_U4 or Code.Ldelem_I8
        or Code.Ldelem_R4 or Code.Ldelem_R8 or Code.Ldelem_Ref;

    static bool IsStelem(Code code) => code is Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1
        or Code.Stelem_I2 or Code.Stelem_I4 or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8
        or Code.Stelem_Ref;

    /// <summary>
    /// Tipo do elemento a partir do opcode (<c>ldelem.r4</c> -> <c>float</c>).
    /// <c>ldelem.any</c>/<c>stelem.any</c> carregam o tipo no operando.
    /// </summary>
    static Type? ElementOf(Code code, object? operand)
    {
        switch (code)
        {
            case Code.Ldelem_I1: case Code.Stelem_I1: return typeof(sbyte);
            case Code.Ldelem_U1: return typeof(byte);
            case Code.Ldelem_I2: case Code.Stelem_I2: return typeof(short);
            case Code.Ldelem_U2: return typeof(ushort);
            case Code.Ldelem_I4: case Code.Stelem_I4: return typeof(int);
            case Code.Ldelem_U4: return typeof(uint);
            case Code.Ldelem_I8: case Code.Stelem_I8: case Code.Stelem_I: return typeof(long);
            case Code.Ldelem_R4: case Code.Stelem_R4: return typeof(float);
            case Code.Ldelem_R8: case Code.Stelem_R8: return typeof(double);
            case Code.Ldelem_Any:
            case Code.Stelem_Any:
                return operand is TypeReference reference ? ResolveType(reference) : null;
            default: return null;
        }
    }

    /// <summary>TypeReference do mod -> Type do shim (o mesmo mapa dos campos).</summary>
    static Type? ResolveType(TypeReference reference)
    {
        string full = reference.FullName.Replace('/', '+');
        if (StructMap.TryGetValue(full, out Type? found)) return found;
        if (ShimMap.TryGetValue(full, out found)) return found;
        return null;
    }

    /// <summary>
    /// O <c>stelem.i1</c> empilha o valor como <c>int32</c>; <c>Get/Set</c>
    /// esperam <c>bool</c>/<c>byte</c>... A conversão explícita mantém a
    /// verificação do Mono satisfeita (o veredito é o mesmo do original).
    /// </summary>
    static void InsertConv(ILProcessor il, Instruction at, Type element, bool beforeConv)
    {
        OpCode conv = Type.GetTypeCode(element) switch
        {
            TypeCode.Boolean or TypeCode.Byte => OpCodes.Conv_U1,
            TypeCode.SByte => OpCodes.Conv_I1,
            TypeCode.Int16 => OpCodes.Conv_I2,
            TypeCode.UInt16 or TypeCode.Char => OpCodes.Conv_U2,
            _ => OpCodes.Nop,
        };
        if (conv == OpCodes.Nop) return;
        Instruction added = il.Create(conv);
        if (beforeConv) il.InsertBefore(at, added);
        else il.InsertAfter(at, added);
    }

    /// <summary>
    /// Efeito na pilha de avaliação. <c>null</c> = opcode fora da tabela (ou
    /// desvio de fluxo): o método inteiro fica sem reescrita de array.
    /// </summary>
    static int? Delta(Instruction ins)
    {
        switch (ins.OpCode.Code)
        {
            case Code.Nop or Code.Break or Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8
                or Code.Conv_R4 or Code.Conv_R8 or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4 or Code.Conv_U8
                or Code.Conv_R_Un or Code.Conv_Ovf_I1 or Code.Conv_Ovf_U1 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_U2
                or Code.Conv_Ovf_I4 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_U8
                or Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_I2_Un or Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_I8_Un
                or Code.Conv_Ovf_U1_Un or Code.Conv_Ovf_U2_Un or Code.Conv_Ovf_U4_Un or Code.Conv_Ovf_U8_Un
                or Code.Neg or Code.Not or Code.Ldfld or Code.Ldflda or Code.Ldobj or Code.Box or Code.Unbox
                or Code.Unbox_Any or Code.Castclass or Code.Isinst or Code.Newarr or Code.Ldlen
                or Code.Volatile or Code.Readonly or Code.Unaligned or Code.Tail or Code.Constrained
                or Code.Sizeof or Code.Ret or Code.Br or Code.Br_S or Code.Leave or Code.Leave_S
                or Code.Endfinally or Code.Rethrow or Code.Ckfinite or Code.Ldvirtftn
                or Code.Ldind_I1 or Code.Ldind_U1 or Code.Ldind_I2 or Code.Ldind_U2 or Code.Ldind_I4
                or Code.Ldind_U4 or Code.Ldind_I8 or Code.Ldind_R4 or Code.Ldind_R8 or Code.Ldind_Ref
                or Code.Ldind_I or Code.Localloc or Code.Refanyval or Code.Refanytype or Code.Mkrefany:
                return 0;

            case Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S or Code.Ldarga or Code.Ldarga_S
                or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S or Code.Ldloca or Code.Ldloca_S
                or Code.Ldnull or Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2
                or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7
                or Code.Ldc_I4_8 or Code.Ldc_I4_S or Code.Ldc_I4 or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8
                or Code.Ldstr or Code.Ldsfld or Code.Ldsflda or Code.Ldtoken or Code.Dup or Code.Ldftn:
                return 1;

            case Code.Pop or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3 or Code.Stloc or Code.Stloc_S
                or Code.Starg or Code.Starg_S or Code.Stsfld or Code.Initobj or Code.Ldelem_Any or Code.Ldelem_I1
                or Code.Ldelem_U1 or Code.Ldelem_I2 or Code.Ldelem_U2 or Code.Ldelem_I4 or Code.Ldelem_U4
                or Code.Ldelem_I8 or Code.Ldelem_R4 or Code.Ldelem_R8 or Code.Ldelem_Ref or Code.Ceq
                or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un or Code.Add or Code.Add_Ovf
                or Code.Add_Ovf_Un or Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un or Code.Mul or Code.Mul_Ovf
                or Code.Mul_Ovf_Un or Code.Div or Code.Div_Un or Code.Rem or Code.Rem_Un or Code.And or Code.Or
                or Code.Xor or Code.Shl or Code.Shr or Code.Shr_Un or Code.Throw or Code.Ldelema
                or Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S or Code.Switch or Code.Endfilter:
                return -1;

            case Code.Stfld or Code.Stobj or Code.Cpobj
                or Code.Stind_I1 or Code.Stind_I2 or Code.Stind_I4 or Code.Stind_I8 or Code.Stind_R4
                or Code.Stind_R8 or Code.Stind_Ref or Code.Stind_I
                or Code.Beq or Code.Beq_S or Code.Bne_Un or Code.Bne_Un_S
                or Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S or Code.Bgt or Code.Bgt_S
                or Code.Bgt_Un or Code.Bgt_Un_S or Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S
                or Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S:
                return -2;

            case Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1 or Code.Stelem_I2 or Code.Stelem_I4
                or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8 or Code.Stelem_Ref:
                return -3;

            case Code.Call or Code.Callvirt:
                if (ins.Operand is not MethodReference callee) return null;
                int pushed = callee.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                int popped = callee.Parameters.Count + (callee.HasThis ? 1 : 0);
                return pushed - popped;

            case Code.Newobj:
                // `newobj` ALOCA o this, nao o tira da pilha: so os argumentos
                // do .ctor sao consumidos (o HasThis nao conta).
                if (ins.Operand is not MethodReference ctor) return null;
                return 1 - ctor.Parameters.Count;

            default:
                // calli / opcode fora da tabela: simulacao linear nao vale.
                return null;
        }
    }


    static string? OwnerOf(TypeReference declaring)
    {
        if (declaring is GenericInstanceType generic) declaring = generic.ElementType;
        return declaring.FullName.Replace('/', '.');
    }

    static bool IsGameType(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        string ns = dot < 0 ? "" : fullName[..dot];
        // tModLoader 自己的类型（Mod/ModItem/ModPlayer/GlobalNPC/...）都在
        // Terraria.ModLoader 下，由 tmlFacade/tModLoader.dll 提供，**不在 shim
        // 里**。它们不是"游戏类型"，更不能被当成 1.4.5 删掉的类型——否则 mod
        // 里无处不在的 `base.SetDefaults()`/`base.Load()` 等调用会被全部 stub
        //（v31 因此从 38 个空方法暴涨到 594 个，连带 content 变 null）。
        if (ns == "Terraria.ModLoader"
            || ns.StartsWith("Terraria.ModLoader.", StringComparison.Ordinal))
            return false;
        return GameNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));
    }

    static bool IsMissingGameMethod(MethodReference method)
    {
        if (method.DeclaringType is not TypeReference declaring) return false;
        string? owner = OwnerOf(declaring);
        if (owner == null || !IsGameType(owner)) return false;
        if (!ShimMap.TryGetValue(owner, out Type shim) && !StructMap.TryGetValue(owner, out shim))
            return true; // tipo do jogo deletado/ausente no shim
        return !FindMethodBySignature(shim, method);
    }

    /// <summary>
    /// Como <see cref="FindMethod"/> mas com assinatura completa (nome + static/
    /// instance + aridade + tipos dos parâmetros). O <see cref="IsMissingGameMethod"/>
    /// precisa disto: um <c>call</c> a uma SOBRECARGA que não existe mais
    /// (ex.: <c>Item.SetDefaults(int, bool, ItemVariant)</c>,
    /// <c>Main.NewText(string, byte, byte, byte)</c>, <c>SpriteBatch.Draw</c> de
    /// 9 parâmetros) passa no teste por NOME — o nome existe, mas aquela
    /// assinatura foi removida na 1.4.5. No aparelho o Mono não consegue ligar
    /// o <c>call</c> e derruba o processo no PrepareMethod (era o crash do v30).
    /// Só comparar nome deixa esses furos passarem.
    /// </summary>
    static bool FindMethodBySignature(Type type, MethodReference method)
    {
        int arity = method.Parameters.Count;
        bool wantStatic = !method.HasThis;
        // `.ctor` NÃO aparece em GetMethods(): é ConstructorInfo. Comparar só
        // GetMethods fazia TODOS os `new Vector2(...)`, `new Rectangle(...)`,
        // `new UIState()` parecerem "apagados na 1.4.5" e esvaziava cada
        // método que constrói alguma coisa. Construtores são sempre instance.
        if (method.Name == ".ctor" && !wantStatic)
        {
            foreach (ConstructorInfo ctor in type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                ParameterInfo[] cps = ctor.GetParameters();
                if (cps.Length != arity) continue;
                bool ok = true;
                for (int i = 0; i < arity; i++)
                    if (!TypeNamesMatch(CecilTypeName(method.Parameters[i].ParameterType), ClrTypeName(cps[i].ParameterType)))
                    { ok = false; break; }
                if (ok) return true;
            }
            // Structs (Vector2, Rectangle) não declaram .ctor() algum: o CLR
            // cria com `initobj`. O `newobj` do mod é legítimo do mesmo jeito.
            if (type.IsValueType) return true;
            return false;
        }
        for (Type? current = type; current != null; current = current.BaseType)
        {
            foreach (MethodInfo m in current.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != method.Name || m.IsStatic != wantStatic) continue;
                // O tipo de retorno faz parte da assinatura na resolução de
                // método do CLR, mas compará-lo como STRING EXATA era uma
                // armadilha: o shim devolve proxies (`GameArray<T>` por `T[]`,
                // `SoundStyle` por `LegacySoundStyle`...) e métodos genéricos
                // aparecem fechados no Cecil (`GetModPlayer<FargoPlayer>`) e
                // abertos na reflexão (`T`). Cada uma dessas diferenças era
                // lida como "método apagado na 1.4.5" -> `unresolved` -> o
                // método INTEIRO virava `ret`. Só comparamos quando os dois
                // lados são tipos concretos e não genéricos; genérico casa com
                // qualquer coisa (o JIT do Mono resolve a instância depois).
                if (!TypeNamesMatch(CecilTypeName(method.ReturnType), ClrTypeName(m.ReturnType)))
                    continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != arity) continue;
                bool match = true;
                for (int i = 0; i < arity; i++)
                {
                    if (!TypeNamesMatch(
                        CecilTypeName(method.Parameters[i].ParameterType),
                        ClrTypeName(ps[i].ParameterType)))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            if (current.BaseType == null || current.BaseType == typeof(object)) break;
        }
        return false;
    }

    /// <summary>
    /// Compara dois nomes canónicos tolerando as diferenças ESTRUTURAIS entre
    /// o IL do PC e o shim: parâmetro genérico casa com qualquer tipo fechado,
    /// e um tipo que no shim virou proxy (<c>GameArray&lt;T&gt;</c>,
    /// <c>GameObject</c>, <c>SoundStyle</c>...) casa com o tipo original.
    ///
    /// Sem isto, `SetFactory.CreateCustomSet&lt;T&gt;`, `Player.GetModPlayer&lt;T&gt;`,
    /// `Vector2..ctor` e `Asset&lt;T&gt;.get_Value` eram classificados como
    /// "apagados na 1.4.5" e esvaziavam 190 métodos — inclusive o `Load()` do
    /// mod, que inicializa todos os singletons. O aparelho então mostrava NRE
    /// em quase tudo, itens em branco e NPC sem interação, SEM crash nativo.
    /// </summary>
    static bool TypeNamesMatch(string cecil, string clr)
    {
        if (string.Equals(cecil, clr, StringComparison.Ordinal)) return true;
        // Parâmetro genérico (`!T`) ou instância genérica cujo argumento foi
        // fechado de um lado só: a definição é a mesma, o JIT resolve depois.
        if (cecil.StartsWith("!", StringComparison.Ordinal) || clr.StartsWith("!", StringComparison.Ordinal))
            return true;
        int a = cecil.IndexOf('<'), b = clr.IndexOf('<');
        if (a >= 0 || b >= 0)
        {
            string headA = a >= 0 ? cecil[..a] : cecil;
            string headB = b >= 0 ? clr[..b] : clr;
            if (string.Equals(headA, headB, StringComparison.Ordinal)) return true;
        }
        // O shim substitui tipos do jogo por proxies de interop. O `call` do
        // mod continua válido: a reescrita de campo/campo-por-propriedade já
        // cuida da tradução, e o Mono liga o método pelo nome.
        if (clr.Contains("BunnyLoader.", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Nome canónico de um tipo Cecil para comparar com <see cref="ClrTypeName"/>.</summary>
    static string CecilTypeName(TypeReference type)
    {
        switch (type)
        {
            case GenericParameter gp:
                return "!" + gp.Name;
            case GenericInstanceType gi:
                return CecilTypeName(gi.ElementType) + "<" + string.Join(",", gi.GenericArguments.Select(CecilTypeName)) + ">";
            case ArrayType arr:
                return CecilTypeName(arr.ElementType) + "[]";
            case ByReferenceType br:
                return CecilTypeName(br.ElementType) + "&";
            case PointerType ptr:
                return CecilTypeName(ptr.ElementType) + "*";
            default:
                return (type.FullName ?? type.Name).Replace('/', '.').Replace('+', '.');
        }
    }

    /// <summary>Nome canónico de um tipo CLR para comparar com <see cref="CecilTypeName"/>.</summary>
    static string ClrTypeName(Type type)
    {
        if (type.IsGenericParameter) return "!" + type.Name;
        if (type.IsByRef) return ClrTypeName(type.GetElementType()!) + "&";
        if (type.IsPointer) return ClrTypeName(type.GetElementType()!) + "*";
        if (type.IsArray) return ClrTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            return (def.FullName ?? def.Name).Replace('+', '.') + "<"
                + string.Join(",", type.GetGenericArguments().Select(ClrTypeName)) + ">";
        }
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    static PropertyInfo? FindProperty(Type type, string name)
    {
        // Propriedades herdadas contam (um mod pode referir Item::width
        // mesmo que o campo seja declarado em Entity no PC).
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? found = current.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (found != null) return found;
            if (current.BaseType == null || current.BaseType == typeof(object)) break;
        }
        return null;
    }

    /// <summary>
    /// Campo de STRUCT proxy (PlayerDrawSet, DrawData, ...): a pilha do IL tem
    /// o ENDERECO do struct do jogo, nao um objeto. `ldobj` troca o endereco
    /// pelo valor do proxy (ele guarda so o ponteiro) e o getter estatico do
    /// shim le o campo pela classe real.
    ///
    /// Antes disto o struct ficava sem reescrita: o campo virava
    /// <c>MissingFieldException</c> no JIT e o metodo inteiro era esvaziado.
    /// Foi o que apareceu assim que <c>Entity.active</c> passou a funcionar —
    /// <c>PlayerDrawSet.drawPlayer</c> derrubava
    /// <c>FargoPlayerBuffDrawLayer.GetDefaultVisibility</c>.
    /// </summary>
    static bool TryRewriteStructField(ModuleDefinition module, ILProcessor il, Instruction ins, FieldReference field, Type structShim)
    {
        // So leitura. `stfld` precisaria reordenar (self, valor) na pilha, o
        // que exige variavel temporaria; nenhum mod escreve em struct do jogo
        // (sao estruturas montadas pelo proprio jogo).
        if (ins.OpCode.Code != Code.Ldfld) return false;

        MethodInfo? getter = FindMethod(structShim, "__get_" + field.Name);
        if (getter == null || !getter.IsStatic || getter.ReturnType.IsByRef) return false;
        ParameterInfo[] parameters = getter.GetParameters();
        if (parameters.Length != 1) return false;

        TypeReference proxy = module.ImportReference(structShim);
        il.InsertBefore(ins, il.Create(OpCodes.Ldobj, proxy));
        // O acessador do ProxyGen recebe IGameObject; struct como argumento de
        // interface precisa de `box` explicito em IL escrito a mao.
        if (parameters[0].ParameterType.IsInterface || parameters[0].ParameterType == typeof(object))
            il.InsertBefore(ins, il.Create(OpCodes.Box, proxy));

        ins.OpCode = OpCodes.Call;
        ins.Operand = module.ImportReference(getter);
        return true;
    }

    static MethodInfo? FindMethod(Type type, string name)
    {
        // `GetMethod(name)` lança AmbiguousMatchException quando o tipo tem
        // sobrecargas com o mesmo nome (SetDefaults, Update, Draw, AI...).
        // O `IsMissingGameMethod` chama isto para TODO call/callvirt; uma
        // exceção aqui abortava o PatchMethod inteiro (o método ficava SEM
        // reescrita de campo) — era a regressão 162 -> 197. Aqui a busca é
        // só por NOME (o objetivo é saber se o membro existe), então a
        // primeira ocorrência resolve sem ambiguidade.
        for (Type? current = type; current != null; current = current.BaseType)
        {
            MethodInfo? found = current
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name);
            if (found != null) return found;
            if (current.BaseType == null || current.BaseType == typeof(object)) break;
        }
        return null;
    }

    static bool HasRealField(Type type, string name) => FindField(type, name) != null;

    static FieldInfo? FindField(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? found = current.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (found != null) return found;
            if (current.BaseType == null || current.BaseType == typeof(object)) break;
        }
        return null;
    }
}
