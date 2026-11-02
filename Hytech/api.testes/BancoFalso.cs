using System.Reflection;
using System.Text.Json;
using HytechApi.Data;

namespace HytechApi.Testes;

// BANCO FALSO em memória (sem SQL Server): usado pelos testes e pelo --servidor-falso.
// Imita as procedures de verdade (inclusive os RAISERROR, que viram ServicoException).

public class Usuario
{
    public int IdUsuario; public string Nome = "", Nickname = "", Email = "", Status = "ativo", Papel = "aluno", SenhaHash = "";
    public int? IdAluno, IdProfessor; public string? Especialidade, FotoUrl;
}
public interface IConteudo { int Id { get; } int? IdProfessor { get; } string Status { get; set; } DateTime? Data { get; set; } string? Motivo { get; set; } }
public class Aula : IConteudo
{
    public int IdAula, IdMateria; public int? IdProfessor, Ordem; public string? Topico, Titulo, Conteudo, ExemploCodigo, DicaTitulo, DicaCorpo;
    public int IsExtra, ChavesRecompensa;
    public string Status { get; set; } = "aprovado"; public DateTime? Data { get; set; } public string? Motivo { get; set; }
    public int Id => IdAula; int? IConteudo.IdProfessor => IdProfessor;
}
public class Questao : IConteudo
{
    public int IdQuestao, IdMateria; public int? IdProfessor, ChavesRecompensa; public string? Enunciado, CodigoExemplo, Dificuldade;
    public string Status { get; set; } = "aprovado"; public DateTime? Data { get; set; } public string? Motivo { get; set; }
    public int Id => IdQuestao; int? IConteudo.IdProfessor => IdProfessor;
}
public class Alternativa { public int Id, IdQuestao; public string Texto = ""; public bool Correta; }
public class Desafio : IConteudo
{
    public int IdDesafio, IdMateria; public int? IdProfessor; public string? Titulo, Enunciado, Dificuldade, CodigoBase, Dica;
    public string Status { get; set; } = "aprovado"; public DateTime? Data { get; set; } public string? Motivo { get; set; }
    public int Id => IdDesafio; int? IConteudo.IdProfessor => IdProfessor;
}
public class Materia { public int IdMateria, Ordem, ChavesParaDesbloquear; public string Titulo = ""; }
public class EstadoAluno
{
    public int Pontos, Gastas, Streak;
    public HashSet<int> Aulas = new(), Questoes = new(), Desbloq = new();
    public Dictionary<int, DateTime> Certs = new();
}
class Ticket { public int IdTicket, Solicitante, Destinatario; public int? IdMateria; public string Assunto = "", Descricao = "", Status = "aberto"; public DateTime DataAbertura; }
class Mensagem { public int Id, IdTicket, Remetente; public string Texto = ""; public DateTime DataHora; }
public class Codigo
{
    public int IdCodigo; public string Finalidade = "", Chave = "", CodigoHash = ""; public int? IdUsuario; public string? Dados;
    public DateTime CriadoEm, ExpiraEm; public int Tentativas; public bool Usado;
}

public sealed class BancoFalso : IDb
{
    public const int ChavesQuestao = 7;       // diferente do padrão (5) de propósito: prova que o site lê do banco
    const int ChavesCertificado = 50;
    static readonly Dictionary<string, int> ChavesDesafio = new() { ["Fácil"] = 5, ["Médio"] = 10, ["Difícil"] = 15 };

    public readonly List<Usuario> Usuarios = new();
    public readonly List<Materia> Materias = new();
    public readonly List<Aula> Aulas = new();
    public readonly List<Questao> Questoes = new();
    public readonly List<Alternativa> Alternativas = new();
    public readonly List<Desafio> Desafios = new();
    public readonly List<Codigo> Codigos = new();
    public readonly List<(string Proc, Dictionary<string, object?> P)> Chamadas = new();

    readonly List<Ticket> _tickets = new();
    readonly List<Mensagem> _msgs = new();
    readonly Dictionary<int, EstadoAluno> _alunos = new();
    readonly HashSet<string> _alunoDesafio = new();
    readonly object _trava = new();
    int _proxId = 100, _proxAula = 1, _proxQuestao = 1, _proxAlt = 1, _proxDesafio = 1, _proxTicket = 1, _proxMsg = 1, _proxCodigo = 1;
    DateTime _relogio = DateTime.UtcNow;
    TimeSpan _desloc = TimeSpan.Zero;

    DateTime Agora() => _relogio = _relogio.AddSeconds(1);   // sempre avança (ordem estável nos testes)
    DateTime AgoraReal() => DateTime.UtcNow + _desloc;

    /// <summary>Simula a passagem do tempo (cooldown, expiração dos códigos).</summary>
    public void AvancarTempo(int segundos) => _desloc += TimeSpan.FromSeconds(segundos);

    public EstadoAluno EstadoDe(int id) { if (!_alunos.TryGetValue(id, out var e)) _alunos[id] = e = new EstadoAluno(); return e; }
    int Saldo(int id) => EstadoDe(id).Pontos - EstadoDe(id).Gastas;

    public BancoFalso(string senhaPadrao = "senha1234", int custoHash = 4)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(senhaPadrao, custoHash);
        Usuarios.Add(new() { IdUsuario = 3, Nome = "Pedro Bala", Nickname = "@PedroBala", Email = "aluno@hytech.com", Papel = "aluno", IdAluno = 1, SenhaHash = hash });
        Usuarios.Add(new() { IdUsuario = 2, Nome = "Prof. Silva", Nickname = "@profsilva", Email = "professor@hytech.com", Papel = "professor", IdProfessor = 1, Especialidade = "Programação", SenhaHash = hash });
        Usuarios.Add(new() { IdUsuario = 1, Nome = "Administrador", Nickname = "@admin", Email = "admin@hytech.com", Papel = "admin", SenhaHash = hash });
        Usuarios.Add(new() { IdUsuario = 9, Nome = "Conta Inativa", Nickname = "@off", Email = "inativo@hytech.com", Status = "inativo", Papel = "aluno", IdAluno = 8, SenhaHash = hash });

        // Alunos fictícios para o ranking não ficar vazio
        var ficticios = new (string, int)[] { ("Alberto Caeiro", 76), ("Álvaro de Campos", 62), ("Ricardo Reis", 51), ("Bernardo Soares", 39), ("Charles R. Anon", 24) };
        for (var i = 0; i < ficticios.Length; i++)
        {
            var (nome, pts) = ficticios[i];
            Usuarios.Add(new() { IdUsuario = 200 + i, Nome = nome, Nickname = "@" + nome.Split(' ')[0].ToLowerInvariant(), Email = $"ranking{i}@hytech.com", Papel = "aluno", IdAluno = 20 + i, SenhaHash = hash });
            EstadoDe(20 + i).Pontos = pts;
        }

        // Conteúdo das matérias (o mesmo que foi importado no SQL)
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("conteudo-falso.json")!;
        using var doc = JsonDocument.Parse(s);
        foreach (var m in doc.RootElement.GetProperty("M").EnumerateObject().OrderBy(x => int.Parse(x.Name)))
        {
            var idMat = int.Parse(m.Name);
            Materias.Add(new() { IdMateria = idMat, Titulo = m.Value.GetProperty("nome").GetString()!, Ordem = idMat, ChavesParaDesbloquear = m.Value.GetProperty("chavesParaDesbloquear").GetInt32() });
            var i = 0;
            foreach (var t in m.Value.GetProperty("topicos").EnumerateArray())
            {
                i++;
                var tit = t.GetProperty("titulo").GetString()!;
                string? topico = null; var titulo = tit;
                if (tit.Contains('—')) { var p = tit.Split('—'); topico = p[0].Trim(); titulo = p[1].Trim(); }
                var dica = t.GetProperty("dica");
                Aulas.Add(new()
                {
                    IdAula = _proxAula++, IdMateria = idMat, Topico = topico, Titulo = titulo, Conteudo = t.GetProperty("conteudo").GetString(),
                    ExemploCodigo = t.GetProperty("exemplo").GetString(), DicaTitulo = dica.GetProperty("titulo").GetString(), DicaCorpo = dica.GetProperty("corpo").GetString(),
                    Ordem = i, IsExtra = t.TryGetProperty("isExtra", out var ex) && ex.ValueKind == JsonValueKind.True ? 1 : 0, ChavesRecompensa = t.GetProperty("chaves").GetInt32()
                });
            }
            foreach (var q in m.Value.GetProperty("questoes").EnumerateArray())
            {
                var id = _proxQuestao++;
                Questoes.Add(new() { IdQuestao = id, IdMateria = idMat, Enunciado = q.GetProperty("pergunta").GetString(), CodigoExemplo = q.GetProperty("codigo").GetString(), Dificuldade = "Fácil", ChavesRecompensa = ChavesQuestao });
                foreach (var a in q.GetProperty("alternativas").EnumerateArray())
                    Alternativas.Add(new() { Id = _proxAlt++, IdQuestao = id, Texto = a.GetProperty("texto").GetString()!, Correta = a.GetProperty("correta").GetBoolean() });
            }
        }
    }

    // ── Simulam o admin do HytechAdmin (só existem no banco falso) ──
    public void Aprovar(string tipo, int id)
    {
        var x = Achar(tipo, id)!; x.Status = "aprovado"; x.Motivo = null;
        if (x is Questao q && q.ChavesRecompensa is null) q.ChavesRecompensa = ChavesQuestao;
    }
    public void Rejeitar(string tipo, int id, string? motivo) { var x = Achar(tipo, id)!; x.Status = "rejeitado"; x.Motivo = motivo; }

    // ── Auxiliares ──
    static Linha L(params (string, object?)[] kv) => new(kv.ToDictionary(k => k.Item1, k => k.Item2));
    static bool Aprovado(IConteudo x) => x.Status == "aprovado";
    string MatDe(int id) => Materias.FirstOrDefault(m => m.IdMateria == id)?.Titulo ?? "";
    string PapelDe(int id) => Usuarios.FirstOrDefault(u => u.IdUsuario == id)?.Papel ?? "";
    string NomeDe(int id) => Usuarios.FirstOrDefault(u => u.IdUsuario == id)?.Nome ?? "";
    Usuario? ProfPorIdProf(int? id) => Usuarios.FirstOrDefault(u => u.Papel == "professor" && u.IdProfessor == id);
    static bool Participa(Ticket? t, int uid) => t is not null && (t.Solicitante == uid || t.Destinatario == uid);
    DateTime UltimaAtividade(Ticket t) => _msgs.Where(m => m.IdTicket == t.IdTicket).Select(m => m.DataHora).DefaultIfEmpty(t.DataAbertura).Max();
    IConteudo? Achar(string tipo, int id) => tipo switch
    {
        "aula" => Aulas.FirstOrDefault(a => a.IdAula == id),
        "questao" => Questoes.FirstOrDefault(q => q.IdQuestao == id),
        "desafio" => Desafios.FirstOrDefault(d => d.IdDesafio == id),
        _ => null
    };
    static string Rotulo(string tipo) => tipo switch { "aula" => "Aula", "questao" => "Questão", _ => "Desafio" };

    bool TentarCertificado(int idAluno, int idMateria)
    {
        var e = EstadoDe(idAluno);
        if (e.Certs.ContainsKey(idMateria)) return false;
        var obrig = Aulas.Where(a => a.IdMateria == idMateria && a.IsExtra == 0).ToList();
        var questoes = Questoes.Where(q => q.IdMateria == idMateria).ToList();
        if (obrig.Count == 0) return false;
        if (!obrig.All(a => e.Aulas.Contains(a.IdAula))) return false;
        if (!questoes.All(q => e.Questoes.Contains(q.IdQuestao))) return false;
        e.Certs[idMateria] = DateTime.UtcNow; e.Pontos += ChavesCertificado;
        return true;
    }
    static int Fases(EstadoAluno e) => e.Aulas.Count + e.Questoes.Count;
    int Posicao(int id) => 1 + _alunos.Count(kv => Usuarios.FirstOrDefault(u => u.IdAluno == kv.Key) is { Status: "ativo" } && kv.Value.Pontos > EstadoDe(id).Pontos);
    static bool Desbloqueada(EstadoAluno e, Materia m) => m.ChavesParaDesbloquear == 0 || e.Desbloq.Contains(m.IdMateria);

    void EnviarParaAprovacao(string tipo, int id, int? idProf)
    {
        var x = Achar(tipo, id);
        if (x is null || x.IdProfessor != idProf || x.Status is not ("rascunho" or "rejeitado"))
            throw new ServicoException("Item não encontrado, não é seu, ou já foi enviado.");
        if (tipo == "questao")
        {
            var alts = Alternativas.Where(a => a.IdQuestao == id && a.Texto.Trim().Length > 0).ToList();
            if (alts.Count < 2) throw new ServicoException("Preencha pelo menos 2 alternativas.");
            if (!alts.Any(a => a.Correta)) throw new ServicoException("Marque a alternativa correta.");
        }
        x.Status = "pendente"; x.Data = Agora(); x.Motivo = null;   // trigger cria a Aprovacao
    }
    static bool PodeEditar(IConteudo? x, int? idProf) => x is not null && x.IdProfessor == idProf && x.Status is "rascunho" or "rejeitado";

    void GravarAlternativas(Questao q, string json, int idxCorreta)
    {
        Alternativas.RemoveAll(a => a.IdQuestao == q.IdQuestao);
        var lista = JsonSerializer.Deserialize<List<string>>(json) ?? new();
        for (var i = 0; i < lista.Count; i++)
            if (lista[i].Trim().Length > 0) Alternativas.Add(new() { Id = _proxAlt++, IdQuestao = q.IdQuestao, Texto = lista[i].Trim(), Correta = i == idxCorreta });
    }

    Linha CabecalhoTicket(Ticket t) => L(
        ("id_ticket", t.IdTicket), ("assunto", t.Assunto), ("status", t.Status), ("data_abertura", t.DataAbertura),
        ("id_materia", t.IdMateria), ("materia", t.IdMateria is int m ? MatDe(m) : null),
        ("id_usuario_solicitante", t.Solicitante), ("solicitante", NomeDe(t.Solicitante)),
        ("id_usuario_destinatario", t.Destinatario), ("destinatario", NomeDe(t.Destinatario)),
        ("papel_destinatario", PapelDe(t.Destinatario)), ("ultima_atividade", UltimaAtividade(t)));

    // ── Entrada ──
    public async Task<List<Linha>> ExecAsync(string proc, params Param[] ps)
    {
        await Task.Yield();
        lock (_trava)
        {
            var p = ps.ToDictionary(x => x.Nome, x => x.Valor);
            Chamadas.Add((proc, p));
            return Executar(proc, p);
        }
    }

    public async Task<List<List<Linha>>> ExecMultiAsync(string proc, params Param[] ps)
    {
        await Task.Yield();
        lock (_trava)
        {
            var p = ps.ToDictionary(x => x.Nome, x => x.Valor);
            Chamadas.Add((proc, p));
            return ExecutarMulti(proc, p);
        }
    }

    static int I(Dictionary<string, object?> p, string k) => p.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v) : 0;
    static int? IN(Dictionary<string, object?> p, string k) => p.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v) : null;
    static string? S(Dictionary<string, object?> p, string k) => p.TryGetValue(k, out var v) ? v as string : null;

    List<List<Linha>> ExecutarMulti(string proc, Dictionary<string, object?> p)
    {
        if (proc == "sp_WebListarMeusConteudos")
        {
            var idp = IN(p, "id_professor");
            var aulas = Aulas.Where(a => a.IdProfessor == idp).Select(a => L(("id_aula", a.IdAula), ("id_materia", a.IdMateria), ("materia", MatDe(a.IdMateria)),
                ("topico", a.Topico), ("titulo", a.Titulo), ("conteudo", a.Conteudo), ("exemplo_codigo", a.ExemploCodigo), ("dica_titulo", a.DicaTitulo),
                ("dica_corpo", a.DicaCorpo), ("status", a.Status), ("data", a.Data), ("motivo", a.Motivo))).Reverse().ToList();
            var questoes = Questoes.Where(q => q.IdProfessor == idp).Select(q => L(("id_questao", q.IdQuestao), ("id_materia", q.IdMateria), ("materia", MatDe(q.IdMateria)),
                ("enunciado", q.Enunciado), ("dificuldade", q.Dificuldade), ("codigo_exemplo", q.CodigoExemplo), ("status", q.Status), ("data", q.Data ?? Agora()), ("motivo", q.Motivo))).Reverse().ToList();
            var alts = Alternativas.Where(a => Questoes.FirstOrDefault(q => q.IdQuestao == a.IdQuestao)?.IdProfessor == idp)
                .Select(a => L(("id_alternativa", a.Id), ("id_questao", a.IdQuestao), ("texto", a.Texto), ("correta", a.Correta))).ToList();
            var desafios = Desafios.Where(d => d.IdProfessor == idp).Select(d => L(("id_desafio", d.IdDesafio), ("id_materia", d.IdMateria), ("materia", MatDe(d.IdMateria)),
                ("titulo", d.Titulo), ("enunciado", d.Enunciado), ("dica", d.Dica), ("dificuldade", d.Dificuldade), ("codigo_base", d.CodigoBase),
                ("status", d.Status), ("data", d.Data ?? Agora()), ("motivo", d.Motivo))).Reverse().ToList();
            return new() { aulas, questoes, alts, desafios };
        }
        if (proc != "sp_ProgressoAluno") throw new NotSupportedException("ExecMulti não simulado para " + proc);
        var e = EstadoDe(I(p, "id_aluno"));
        return new()
        {
            e.Aulas.Select(id => L(("id_aula", id))).ToList(),
            e.Questoes.Select(id => L(("id_questao", id))).ToList(),
            Materias.Select(m => L(("id_materia", m.IdMateria), ("titulo", m.Titulo), ("ordem", m.Ordem), ("desbloqueada", Desbloqueada(e, m)))).ToList(),
            e.Certs.Select(c => L(("id_materia", c.Key), ("data_emissao", c.Value))).ToList()
        };
    }

    List<Linha> Executar(string proc, Dictionary<string, object?> p)
    {
        switch (proc)
        {
            case "sp_ObterUsuarioParaLogin":
            {
                var email = (S(p, "email") ?? "").Trim().ToLowerInvariant();
                return Usuarios.Where(u => u.Email == email).Select(u => L(("id_usuario", u.IdUsuario), ("nome", u.Nome), ("nickname", u.Nickname),
                    ("email", u.Email), ("status", u.Status), ("papel", u.Papel), ("id_aluno", u.IdAluno), ("id_professor", u.IdProfessor),
                    ("id_admin", u.Papel == "admin" ? u.IdUsuario : null), ("senha_hash", u.SenhaHash), ("foto_url", u.FotoUrl))).ToList();
            }
            case "sp_RegistrarLogin": return new();
            case "sp_VerificarDisponibilidade":
            {
                var email = S(p, "email")?.Trim().ToLowerInvariant();
                var nk = S(p, "nickname")?.Trim();
                if (nk is not null && !nk.StartsWith('@')) nk = "@" + nk;
                var ign = IN(p, "id_usuario_ignorar");
                bool Outro(Usuario u) => ign is null || u.IdUsuario != ign;
                return new() { L(("email_em_uso", email is not null && Usuarios.Any(u => u.Email == email && Outro(u))),
                                 ("nickname_em_uso", nk is not null && Usuarios.Any(u => u.Nickname == nk && Outro(u)))) };
            }
            case "sp_CriarCodigoVerificacao":
            {
                var fin = S(p, "finalidade")!; var chave = S(p, "chave")!;
                var mesmos = Codigos.Where(c => c.Finalidade == fin && c.Chave == chave).ToList();
                var agora = AgoraReal();
                if (mesmos.Any(c => c.CriadoEm > agora.AddSeconds(-30))) throw new ServicoException("Aguarde alguns segundos para pedir outro código.");
                if (mesmos.Count(c => c.CriadoEm > agora.AddHours(-1)) >= 5) throw new ServicoException("Muitos códigos solicitados. Tente novamente mais tarde.");
                foreach (var c in mesmos) c.Usado = true;
                var novo = new Codigo { IdCodigo = _proxCodigo++, Finalidade = fin, Chave = chave, IdUsuario = IN(p, "id_usuario"), CodigoHash = S(p, "codigo_hash")!,
                    Dados = S(p, "dados"), CriadoEm = agora, ExpiraEm = agora.AddMinutes(IN(p, "minutos") ?? 10) };
                Codigos.Add(novo);
                return new() { L(("id_codigo", novo.IdCodigo)) };
            }
            case "sp_ObterCodigoVerificacao":
            {
                var incluir = IN(p, "incluir_expirados") == 1;
                var agora = AgoraReal();
                var c = Codigos.LastOrDefault(x => x.Finalidade == S(p, "finalidade") && x.Chave == S(p, "chave") && !x.Usado
                    && (incluir || (x.ExpiraEm > agora && x.Tentativas < 5)));
                return c is null ? new() : new() { L(("id_codigo", c.IdCodigo), ("id_usuario", c.IdUsuario), ("codigo_hash", c.CodigoHash), ("dados", c.Dados), ("tentativas", c.Tentativas), ("expira_em", c.ExpiraEm)) };
            }
            case "sp_RegistrarTentativaCodigo": Codigos.First(x => x.IdCodigo == I(p, "id_codigo")).Tentativas++; return new();
            case "sp_ConsumirCodigo": Codigos.First(x => x.IdCodigo == I(p, "id_codigo")).Usado = true; return new();
            case "sp_DesativarConta":
            {
                var u = Usuarios.FirstOrDefault(x => x.IdUsuario == I(p, "id_usuario")) ?? throw new ServicoException("Usuário não encontrado.");
                if (u.Papel == "admin") throw new ServicoException("Administradores não podem desativar a conta por aqui.");
                u.Status = "inativo"; return new();
            }
            case "sp_CadastrarAluno":
            {
                var email = S(p, "email")!.Trim().ToLowerInvariant();
                if (Usuarios.Any(u => u.Email == email)) throw new ServicoException("Este e-mail já está cadastrado.");
                var nick = S(p, "nickname")!; if (!nick.StartsWith('@')) nick = "@" + nick;
                if (Usuarios.Any(u => u.Nickname == nick)) throw new ServicoException("Este nome de usuário já está em uso.");
                var id = ++_proxId;
                Usuarios.Add(new() { IdUsuario = id, Nome = S(p, "nome")!, Nickname = nick, Email = email, Papel = "aluno", IdAluno = id, SenhaHash = S(p, "senha_hash")! });
                return new() { L(("id_usuario", id), ("id_aluno", id)) };
            }
            case "sp_ObterConfiguracaoPlataforma":
                return new() { L(("chaves_questao_facil", ChavesQuestao), ("chaves_questao_media", 10), ("chaves_questao_dificil", 15), ("chaves_certificado", ChavesCertificado)) };
            case "sp_WebListarMaterias":
                return Materias.Select(m => L(("id_materia", m.IdMateria), ("titulo", m.Titulo), ("ordem", m.Ordem), ("chaves_para_desbloquear", m.ChavesParaDesbloquear))).ToList();
            case "sp_WebListarAulasMateria":
                return Aulas.Where(a => a.IdMateria == I(p, "id_materia") && Aprovado(a)).Select(a => L(("id_aula", a.IdAula), ("topico", a.Topico), ("titulo", a.Titulo),
                    ("conteudo", a.Conteudo), ("exemplo_codigo", a.ExemploCodigo), ("dica_titulo", a.DicaTitulo), ("dica_corpo", a.DicaCorpo), ("ordem", a.Ordem),
                    ("is_extra", a.IsExtra), ("chaves_recompensa", a.ChavesRecompensa), ("autor", a.IdProfessor is not null ? ProfPorIdProf(a.IdProfessor)?.Nome : null))).ToList();
            case "sp_WebListarQuestoesMateria":
                return Questoes.Where(q => q.IdMateria == I(p, "id_materia") && Aprovado(q)).Select(q => L(("id_questao", q.IdQuestao), ("enunciado", q.Enunciado),
                    ("codigo_exemplo", q.CodigoExemplo), ("dificuldade", q.Dificuldade), ("chaves_recompensa", q.ChavesRecompensa))).ToList();
            case "sp_WebListarAlternativasMateria":
            {
                var ids = Questoes.Where(q => q.IdMateria == I(p, "id_materia") && Aprovado(q)).Select(q => q.IdQuestao).ToHashSet();
                return Alternativas.Where(a => ids.Contains(a.IdQuestao)).Select(a => L(("id_alternativa", a.Id), ("id_questao", a.IdQuestao), ("texto", a.Texto))).ToList();
            }
            case "sp_ResponderQuestao":
            {
                var idq = I(p, "id_questao");
                var q = Questoes.FirstOrDefault(x => x.IdQuestao == idq) ?? throw new ServicoException("Questão inexistente ou ainda não aprovada.");
                var alt = Alternativas.FirstOrDefault(a => a.Id == I(p, "id_alternativa") && a.IdQuestao == idq) ?? throw new ServicoException("Alternativa não pertence a essa questão.");
                var ida = I(p, "id_aluno"); var e = EstadoDe(ida); var antes = e.Pontos; var ja = e.Questoes.Contains(idq); var cert = false;
                if (alt.Correta && !ja) { e.Questoes.Add(idq); e.Pontos += q.ChavesRecompensa ?? 0; cert = TentarCertificado(ida, q.IdMateria); }
                e.Streak = Math.Max(e.Streak, 1);
                return new() { L(("acertou", alt.Correta), ("ja_concluida", ja), ("chaves_ganhas", e.Pontos - antes), ("certificado_emitido", cert), ("saldo_chaves", Saldo(ida))) };
            }
            case "sp_ConcluirAula":
            {
                var au = Aulas.FirstOrDefault(a => a.IdAula == I(p, "id_aula")) ?? throw new ServicoException("Aula inexistente ou ainda não aprovada.");
                var ida = I(p, "id_aluno"); var e = EstadoDe(ida); var antes = e.Pontos; var cert = false;
                if (e.Aulas.Add(au.IdAula)) { e.Pontos += au.ChavesRecompensa; cert = TentarCertificado(ida, au.IdMateria); }
                e.Streak = Math.Max(e.Streak, 1);
                return new() { L(("chaves_ganhas", e.Pontos - antes), ("certificado_emitido", cert), ("saldo_chaves", Saldo(ida))) };
            }
            case "sp_DesbloquearMateria":
            {
                var m = Materias.FirstOrDefault(x => x.IdMateria == I(p, "id_materia")) ?? throw new ServicoException("Matéria não encontrada.");
                var ida = I(p, "id_aluno"); var e = EstadoDe(ida);
                if (Desbloqueada(e, m)) return new() { L(("desbloqueada", true), ("chaves_gastas", 0), ("saldo_chaves", Saldo(ida))) };
                var anterior = Materias.Where(x => x.Ordem < m.Ordem).OrderByDescending(x => x.Ordem).FirstOrDefault();
                if (anterior is not null && !e.Certs.ContainsKey(anterior.IdMateria)) throw new ServicoException("Conclua a matéria anterior antes de desbloquear esta.");
                if (Saldo(ida) < m.ChavesParaDesbloquear) throw new ServicoException("Chaves insuficientes.");
                e.Gastas += m.ChavesParaDesbloquear; e.Desbloq.Add(m.IdMateria);
                return new() { L(("desbloqueada", true), ("chaves_gastas", m.ChavesParaDesbloquear), ("saldo_chaves", Saldo(ida))) };
            }
            case "sp_ObterPerfilAluno":
            {
                var ida = I(p, "id_aluno");
                var u = Usuarios.FirstOrDefault(x => x.IdAluno == ida); if (u is null) return new();
                var e = EstadoDe(ida);
                return new() { L(("nome", u.Nome), ("nickname", u.Nickname), ("email", u.Email), ("foto_url", u.FotoUrl), ("data_cadastro", DateTime.UtcNow),
                    ("pontos_acumulados", e.Pontos), ("chaves_saldo", Saldo(ida)), ("streak_atual", e.Streak), ("fases_concluidas", Fases(e)),
                    ("questoes_concluidas", e.Questoes.Count), ("certificados", e.Certs.Count), ("posicao_ranking", Posicao(ida))) };
            }
            case "sp_RankingAlunos":
            {
                var lista = Usuarios.Where(u => u.Papel == "aluno" && u.Status == "ativo" && u.IdAluno is not null)
                    .Select(u => (u, e: EstadoDe(u.IdAluno!.Value))).OrderByDescending(x => x.e.Pontos).ThenBy(x => x.u.Nome, StringComparer.Ordinal).ToList();
                return lista.Select(x => L(("posicao", 1 + lista.Count(y => y.e.Pontos > x.e.Pontos)), ("nome", x.u.Nome), ("nickname", x.u.Nickname),
                    ("foto_url", x.u.FotoUrl), ("pontos_acumulados", x.e.Pontos), ("streak_atual", x.e.Streak), ("fases_concluidas", Fases(x.e)))).Take(IN(p, "top") ?? 10).ToList();
            }
            case "sp_AtualizarPerfil":
            {
                var u = Usuarios.FirstOrDefault(x => x.IdUsuario == I(p, "id_usuario")) ?? throw new ServicoException("Usuário não encontrado.");
                var email = S(p, "email")?.Trim().ToLowerInvariant();
                var nick = S(p, "nickname"); if (nick is not null && !nick.StartsWith('@')) nick = "@" + nick;
                if (email is not null && Usuarios.Any(x => x.Email == email && x.IdUsuario != u.IdUsuario)) throw new ServicoException("Este e-mail já está em uso.");
                if (nick is not null && Usuarios.Any(x => x.Nickname == nick && x.IdUsuario != u.IdUsuario)) throw new ServicoException("Este nome de usuário já está em uso.");
                if (S(p, "nome") is { } n) u.Nome = n; if (nick is not null) u.Nickname = nick; if (email is not null) u.Email = email; if (S(p, "foto_url") is { } f) u.FotoUrl = f;
                return new();
            }
            case "sp_AlterarSenha":
            {
                var u = Usuarios.FirstOrDefault(x => x.IdUsuario == I(p, "id_usuario")) ?? throw new ServicoException("Usuário não encontrado.");
                u.SenhaHash = S(p, "senha_hash")!; return new();
            }
            case "sp_AbrirTicket":
            {
                var dest = IN(p, "id_usuario_destinatario"); var idm = IN(p, "id_materia");
                if (dest is null && idm is not null)
                {
                    var mat = Materias.FirstOrDefault(m => m.IdMateria == idm);
                    var prof = Usuarios.Where(u => u.Papel == "professor" && u.Status == "ativo" && mat is not null && u.Especialidade == mat.Titulo)
                        .OrderBy(a => _tickets.Count(t => t.Destinatario == a.IdUsuario && t.Status != "fechado")).FirstOrDefault();
                    if (prof is not null) dest = prof.IdUsuario;
                }
                if (dest is null) dest = Usuarios.FirstOrDefault(u => u.Papel == "admin" && u.Status == "ativo")?.IdUsuario;
                if (dest is null) throw new ServicoException("Não foi possível definir o destinatário do ticket.");
                var t = new Ticket { IdTicket = _proxTicket++, Solicitante = I(p, "id_usuario_solicitante"), Destinatario = dest.Value, IdMateria = idm,
                    Assunto = S(p, "assunto")!, Descricao = S(p, "descricao")!, DataAbertura = Agora() };
                _tickets.Add(t);
                _msgs.Add(new() { Id = _proxMsg++, IdTicket = t.IdTicket, Remetente = t.Solicitante, Texto = t.Descricao, DataHora = Agora() });
                return new() { L(("id_ticket", t.IdTicket), ("id_usuario_destinatario", dest)) };
            }
            case "sp_ResponderTicket":
            {
                var t = _tickets.FirstOrDefault(x => x.IdTicket == I(p, "id_ticket")) ?? throw new ServicoException("Ticket não encontrado.");
                var rem = I(p, "id_usuario_remetente");
                if (t.Status == "fechado") throw new ServicoException("Ticket encerrado: não aceita novas mensagens.");
                if (!Participa(t, rem) && PapelDe(rem) != "admin") throw new ServicoException("Você não participa deste ticket.");
                _msgs.Add(new() { Id = _proxMsg++, IdTicket = t.IdTicket, Remetente = rem, Texto = S(p, "texto")!, DataHora = Agora() });
                t.Status = rem == t.Solicitante ? "aberto" : "respondido";   // trigger TR_TicketMensagem_AtualizaStatus
                return new();
            }
            case "sp_MensagensTicket":
            {
                var t = _tickets.FirstOrDefault(x => x.IdTicket == I(p, "id_ticket"));
                var uid = IN(p, "id_usuario");
                if (uid is not null && !Participa(t, uid.Value) && PapelDe(uid.Value) != "admin") throw new ServicoException("Você não participa deste ticket.");
                return _msgs.Where(m => m.IdTicket == t!.IdTicket).Select(m => L(("id_mensagem", m.Id), ("id_usuario_remetente", m.Remetente), ("nome", NomeDe(m.Remetente)),
                    ("texto", m.Texto), ("data_hora", m.DataHora), ("remetente", m.Remetente == t!.Solicitante ? "aluno" : "professor"), ("papel", PapelDe(m.Remetente)))).ToList();
            }
            case "sp_WebObterTicket":
            {
                var t = _tickets.FirstOrDefault(x => x.IdTicket == I(p, "id_ticket"));
                if (!Participa(t, I(p, "id_usuario"))) throw new ServicoException("Você não participa deste ticket.");
                return new() { CabecalhoTicket(t!) };
            }
            case "sp_WebListarTickets":
                return _tickets.Where(t => Participa(t, I(p, "id_usuario"))).OrderByDescending(UltimaAtividade).ThenByDescending(t => t.IdTicket).Select(CabecalhoTicket).ToList();
            case "sp_FecharTicket":
            {
                var t = _tickets.FirstOrDefault(x => x.IdTicket == I(p, "id_ticket"));
                var uid = I(p, "id_usuario");
                if (t is null || (t.Destinatario != uid && PapelDe(uid) != "admin")) throw new ServicoException("Ticket não encontrado ou sem permissão.");
                t.Status = "fechado"; return new();
            }
            case "sp_WebObterPerfilProfessor":
            {
                var u = Usuarios.FirstOrDefault(x => x.IdUsuario == I(p, "id_usuario") && x.Papel == "professor"); if (u is null) return new();
                var meus = Aulas.Cast<IConteudo>().Concat(Questoes).Concat(Desafios).Where(x => x.IdProfessor == u.IdProfessor && x.Status != "rascunho").ToList();
                var mat = Materias.FirstOrDefault(m => m.Titulo == u.Especialidade);
                return new() { L(("id_professor", u.IdProfessor), ("nome", u.Nome), ("nickname", u.Nickname), ("email", u.Email), ("foto_url", u.FotoUrl),
                    ("especialidade", u.Especialidade), ("id_materia_especialidade", mat?.IdMateria), ("enviados", meus.Count),
                    ("aprovados", meus.Count(x => x.Status == "aprovado")), ("pendentes", meus.Count(x => x.Status == "pendente")),
                    ("alunos_ativos", Usuarios.Count(x => x.Papel == "aluno" && x.Status == "ativo" && x.IdAluno is not null))) };
            }
            case "sp_WebSalvarAula":
            {
                var idm = I(p, "id_materia"); var idp = IN(p, "id_professor");
                if (!Materias.Any(m => m.IdMateria == idm)) throw new ServicoException("Matéria não encontrada.");
                Aula a;
                if (IN(p, "id_aula") is null) { a = new Aula { IdAula = _proxAula++, IdMateria = idm, IdProfessor = idp, Status = "rascunho" }; Aulas.Add(a); }
                else
                {
                    a = Aulas.FirstOrDefault(x => x.IdAula == I(p, "id_aula"))!;
                    if (!PodeEditar(a, idp)) throw new ServicoException("Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.");
                }
                a.IdMateria = idm; a.Titulo = S(p, "titulo"); a.Topico = S(p, "topico"); a.Conteudo = S(p, "conteudo"); a.ExemploCodigo = S(p, "exemplo_codigo");
                a.DicaTitulo = S(p, "dica_titulo"); a.DicaCorpo = S(p, "dica_corpo"); a.IsExtra = 1; a.ChavesRecompensa = 0;
                if (I(p, "enviar") == 1) EnviarParaAprovacao("aula", a.IdAula, idp);
                return new() { L(("id_aula", a.IdAula)) };
            }
            case "sp_SalvarQuestao":
            {
                var idm = I(p, "id_materia"); var idp = IN(p, "id_professor");
                if (!Materias.Any(m => m.IdMateria == idm)) throw new ServicoException("Matéria não encontrada.");
                Questao q;
                if (IN(p, "id_questao") is null) { q = new Questao { IdQuestao = _proxQuestao++, IdMateria = idm, IdProfessor = idp, Status = "rascunho" }; Questoes.Add(q); }
                else
                {
                    q = Questoes.FirstOrDefault(x => x.IdQuestao == I(p, "id_questao"))!;
                    if (!PodeEditar(q, idp)) throw new ServicoException("Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.");
                }
                q.IdMateria = idm; q.Enunciado = S(p, "enunciado"); q.Dificuldade = S(p, "dificuldade"); q.CodigoExemplo = S(p, "codigo_exemplo"); q.Data = Agora();
                GravarAlternativas(q, S(p, "alternativas_json") ?? "[]", I(p, "indice_correta"));
                if (I(p, "enviar") == 1) EnviarParaAprovacao("questao", q.IdQuestao, idp);
                return new() { L(("id_questao", q.IdQuestao)) };
            }
            case "sp_SalvarDesafio":
            {
                var idp = IN(p, "id_professor");
                Desafio d;
                if (IN(p, "id_desafio") is null) { d = new Desafio { IdDesafio = _proxDesafio++, IdProfessor = idp, Status = "rascunho" }; Desafios.Add(d); }
                else
                {
                    d = Desafios.FirstOrDefault(x => x.IdDesafio == I(p, "id_desafio"))!;
                    if (!PodeEditar(d, idp)) throw new ServicoException("Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.");
                }
                d.IdMateria = I(p, "id_materia"); d.Titulo = S(p, "titulo"); d.Enunciado = S(p, "enunciado"); d.Dificuldade = S(p, "dificuldade");
                d.CodigoBase = S(p, "codigo_base"); d.Dica = S(p, "dica"); d.Data = Agora();
                if (I(p, "enviar") == 1) EnviarParaAprovacao("desafio", d.IdDesafio, idp);
                return new() { L(("id_desafio", d.IdDesafio)) };
            }
            case "sp_EnviarParaAprovacao": EnviarParaAprovacao(S(p, "tipo")!, I(p, "id"), IN(p, "id_professor")); return new();
            case "sp_ExcluirConteudoProfessor":
            {
                var tipo = S(p, "tipo")!; var id = I(p, "id");
                var x = Achar(tipo, id);
                if (x is null || x.IdProfessor != IN(p, "id_professor") || x.Status == "aprovado") throw new ServicoException(Rotulo(tipo) + " não encontrada ou já aprovada.");
                switch (x) { case Aula a: Aulas.Remove(a); break; case Questao q: Questoes.Remove(q); Alternativas.RemoveAll(al => al.IdQuestao == id); break; case Desafio d: Desafios.Remove(d); break; }
                return new();
            }
            case "sp_WebListarDesafiosAluno":
                return Desafios.Where(Aprovado).Select(d => L(("id_desafio", d.IdDesafio), ("id_materia", d.IdMateria), ("materia", MatDe(d.IdMateria)), ("titulo", d.Titulo),
                    ("enunciado", d.Enunciado), ("dica", d.Dica), ("dificuldade", d.Dificuldade), ("codigo_base", d.CodigoBase), ("autor", ProfPorIdProf(d.IdProfessor)?.Nome),
                    ("concluido", _alunoDesafio.Contains(I(p, "id_aluno") + ":" + d.IdDesafio)))).ToList();
            case "sp_WebConcluirDesafio":
            {
                var d = Desafios.FirstOrDefault(x => x.IdDesafio == I(p, "id_desafio"));
                if (d is null || !Aprovado(d)) throw new ServicoException("Desafio indisponível.");
                var ida = I(p, "id_aluno"); var e = EstadoDe(ida);
                var ja = !_alunoDesafio.Add(ida + ":" + d.IdDesafio);
                var ganho = ChavesDesafio.GetValueOrDefault(d.Dificuldade ?? "", 5);
                if (!ja) e.Pontos += ganho;
                return new() { L(("ja_concluido", ja), ("chaves_ganhas", ja ? 0 : ganho), ("saldo_chaves", Saldo(ida))) };
            }
            case "sp_DesempenhoAlunos":
            {
                var idm = IN(p, "id_materia");
                bool DoMat(int? m) => idm is null || m == idm;
                return Usuarios.Where(u => u.Papel == "aluno" && u.Status == "ativo" && u.IdAluno is not null).OrderBy(u => u.Nome, StringComparer.Ordinal).Select(u =>
                {
                    var e = EstadoDe(u.IdAluno!.Value);
                    var f = e.Aulas.Count(id => DoMat(Aulas.FirstOrDefault(a => a.IdAula == id)?.IdMateria)) + e.Questoes.Count(id => DoMat(Questoes.FirstOrDefault(q => q.IdQuestao == id)?.IdMateria));
                    return L(("nome", u.Nome), ("email", u.Email), ("chaves", e.Pontos), ("fases", f), ("acerto", e.Questoes.Count > 0 ? 100 : 0),
                        ("certs", e.Certs.Keys.Count(k => DoMat(k))), ("ultima", e.Streak > 0 ? DateTime.UtcNow.Date : null));
                }).ToList();
            }
            // ── Só o suficiente das rotas de admin para testar permissões ──
            case "sp_MetricasPlataforma":
                return new() { L(("total_usuarios", Usuarios.Count), ("usuarios_ativos", Usuarios.Count(u => u.Status == "ativo")), ("total_alunos", Usuarios.Count(u => u.Papel == "aluno")),
                    ("total_professores", Usuarios.Count(u => u.Papel == "professor")), ("total_admins", Usuarios.Count(u => u.Papel == "admin")), ("pendentes_total", 0),
                    ("certificados_emitidos", _alunos.Values.Sum(a => a.Certs.Count)), ("chaves_distribuidas", (long)_alunos.Values.Sum(a => a.Pontos))) };
            case "sp_ListarMaterias":
                return Materias.Select(m => L(("id_materia", m.IdMateria), ("titulo", m.Titulo), ("icone", ""))).ToList();
            default: throw new NotSupportedException("Procedure não simulada: " + proc);
        }
    }
}
