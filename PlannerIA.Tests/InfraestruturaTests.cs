using ClosedXML.Excel;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Servicos;
using PlannerIA.Infrastructure.Dados;
using PlannerIA.Infrastructure.Importacao;

namespace PlannerIA.Tests;

public sealed class InfraestruturaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "PlannerIA.Tests", Guid.NewGuid().ToString("N"));

    public InfraestruturaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_pasta, recursive: true); } catch { /* arquivo ainda em uso */ }
    }

    /// <summary>Cria uma planilha no mesmo formato da planilha original (Resumo + abas de semana).</summary>
    private string CriarPlanilha()
    {
        var caminho = Path.Combine(_pasta, "objetivos.xlsx");
        using var wb = new XLWorkbook();

        var resumo = wb.AddWorksheet("Resumo");
        resumo.Cell(1, 1).Value = "Objetivos Nov–Dez 2026";

        var s1 = wb.AddWorksheet("Semana 1");
        s1.Cell(1, 1).Value = "Semana 1 — 16 a 22/11";
        s1.Cell(2, 1).Value = "Objetivo: Terminar o jogo atual";
        string[] cab = ["Tipo", "Tarefa", "Dia", "Feito", "Progresso", "Notas"];
        for (var c = 0; c < cab.Length; c++) s1.Cell(5, c + 1).Value = cab[c];
        s1.Cell(6, 1).Value = "Principal"; s1.Cell(6, 2).Value = "Finalizar o jogo atual";
        s1.Cell(6, 4).FormulaA1 = "IF(COUNTIF(D7:D8,\"✔\")=ROWS(D7:D8),\"✔\",\"\")";
        s1.Cell(7, 1).Value = "Sub"; s1.Cell(7, 2).Value = "    Corrigir os bugs"; s1.Cell(7, 3).Value = "Seg 16"; s1.Cell(7, 4).Value = "✔";
        s1.Cell(8, 1).Value = "Sub"; s1.Cell(8, 2).Value = "    Publicar no itch.io"; s1.Cell(8, 3).Value = "Qua 18";
        s1.Cell(10, 1).Value = "Principal"; s1.Cell(10, 2).Value = "Inglês";
        s1.Cell(11, 1).Value = "Sub"; s1.Cell(11, 2).Value = "Listening no ônibus"; s1.Cell(11, 3).Value = "Ter–Qui"; s1.Cell(11, 6).Value = "podcast";

        var s3 = wb.AddWorksheet("Semana 3");
        s3.Cell(1, 1).Value = "Semana 3 — 30/11 a 06/12";
        s3.Cell(2, 1).Value = "Objetivo: Portfólio";
        for (var c = 0; c < cab.Length; c++) s3.Cell(5, c + 1).Value = cab[c];
        s3.Cell(6, 1).Value = "Principal"; s3.Cell(6, 2).Value = "Portfólio no itch.io";
        s3.Cell(7, 1).Value = "Sub"; s3.Cell(7, 2).Value = "Organizar os jogos"; s3.Cell(7, 3).Value = "Sex 04";

        wb.SaveAs(caminho);
        return caminho;
    }

    [Fact]
    public void Importador_le_semanas_tarefas_datas_e_feitos()
    {
        var meses = new ImportadorPlanilhaXlsx().Importar(CriarPlanilha());

        // Semana 3 começa em 30/11 → fica em novembro junto com a semana 1.
        var nov = Assert.Single(meses);
        Assert.Equal("Novembro 2026", nov.Titulo);
        Assert.Equal(2, nov.Filhos.Count);

        var s1 = nov.Filhos.Single(s => s.Inicio == new DateOnly(2026, 11, 16));
        Assert.Equal(new DateOnly(2026, 11, 22), s1.Fim);
        Assert.Equal("Terminar o jogo atual", s1.Objetivo);

        var jogo = s1.TarefasPrincipais.First();
        Assert.Equal("Finalizar o jogo atual", jogo.Titulo);
        Assert.Equal(2, jogo.Subtarefas.Count);
        Assert.Equal("Corrigir os bugs", jogo.Subtarefas[0].Titulo);
        Assert.Equal(new DateOnly(2026, 11, 16), jogo.Subtarefas[0].Data);
        Assert.True(jogo.Subtarefas[0].Concluida);
        Assert.False(jogo.Concluida);

        var listening = s1.TarefasPrincipais.Last().Subtarefas.Single();
        Assert.Null(listening.Data);
        Assert.Equal("Dias: Ter–Qui · podcast", listening.Notas);

        var s3 = nov.Filhos.Single(s => s.Inicio == new DateOnly(2026, 11, 30));
        Assert.Equal(new DateOnly(2026, 12, 6), s3.Fim);
        Assert.Equal(new DateOnly(2026, 12, 4), s3.TarefasPrincipais.Single().Subtarefas.Single().Data);
    }

    [Fact]
    public async Task Importar_salva_no_sqlite_e_recarrega_igual()
    {
        var banco = Path.Combine(_pasta, "planner.db");
        var meses = new ImportadorPlanilhaXlsx().Importar(CriarPlanilha());

        using (var repo = PlanoRepository.CriarParaArquivo(banco))
        {
            var plano = new PlanoService(repo);
            await plano.InicializarAsync();
            var (importadas, ignoradas) = await plano.ImportarAsync(meses);
            Assert.Equal((2, 0), (importadas, ignoradas));

            var sub = plano.Semanas.First().TarefasPrincipais.First().Subtarefas[1];
            await plano.DefinirConclusaoAsync(sub, true);
        }

        using (var repo = PlanoRepository.CriarParaArquivo(banco))
        {
            var plano = new PlanoService(repo);
            await plano.InicializarAsync();

            Assert.Equal(2, plano.Semanas.Count());
            var jogo = plano.Semanas.First().TarefasPrincipais.First();
            Assert.Equal(2, jogo.Subtarefas.Count);
            Assert.True(jogo.Concluida); // as duas subtarefas feitas
            Assert.Equal(new Progresso(2, 3), plano.Semanas.First().Progresso());

            // Importar de novo ignora as semanas que já existem.
            var (importadas, ignoradas) = await plano.ImportarAsync(new ImportadorPlanilhaXlsx().Importar(CriarPlanilha()));
            Assert.Equal((0, 2), (importadas, ignoradas));
        }
    }

    [Fact]
    public async Task Remover_tarefa_principal_remove_subtarefas_do_banco()
    {
        var banco = Path.Combine(_pasta, "remover.db");
        using (var repo = PlanoRepository.CriarParaArquivo(banco))
        {
            var plano = new PlanoService(repo);
            await plano.InicializarAsync();
            var semana = await plano.CriarSemanaAsync(null, null, new(2026, 11, 16), new(2026, 11, 22));
            var t = await plano.CriarTarefaAsync(semana, "Principal", null, null);
            await plano.CriarSubtarefaAsync(t, "Sub", null, null);
            await plano.RemoverTarefaAsync(t);
        }

        using (var repo = PlanoRepository.CriarParaArquivo(banco))
        {
            var plano = new PlanoService(repo);
            await plano.InicializarAsync();
            Assert.Empty(plano.Semanas.Single().Tarefas);
        }
    }

    [Fact]
    public void Planilha_real_do_projeto_importa_sem_erros()
    {
        var real = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Objetivos-Semanais-Nov-Dez.xlsx"));
        if (!File.Exists(real)) return; // a planilha pessoal não vai para o repositório

        var meses = new ImportadorPlanilhaXlsx().Importar(real);
        var semanas = meses.SelectMany(m => m.Filhos).ToList();

        Assert.Equal(6, semanas.Count);
        Assert.Equal([14, 13, 11, 11, 8, 4], semanas.Select(s => s.Progresso().Total));
        Assert.Equal(Datas.NomeDoMes(2026, 11), meses[0].Titulo);
    }
}
