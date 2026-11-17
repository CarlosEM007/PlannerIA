using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;

namespace PlannerIA.Infrastructure.Importacao;

/// <summary>
/// Importa a planilha de objetivos semanais: uma aba por semana com título "Semana 1 — 16 a 22/11",
/// linha "Objetivo: ..." e uma tabela com as colunas Tipo (Principal/Sub), Tarefa, Dia, Feito e Notas.
/// Abas sem intervalo de datas no título (ex.: "Resumo") são ignoradas.
/// </summary>
public partial class ImportadorPlanilhaXlsx(Func<int>? anoPadrao = null) : IImportadorPlanilha
{
    private readonly Func<int> _anoPadrao = anoPadrao ?? (() => DateTime.Today.Year);

    [GeneratedRegex(@"(\d{1,2})(?:/(\d{1,2}))?\s*a\s*(\d{1,2})/(\d{1,2})")]
    private static partial Regex RegexIntervalo();

    [GeneratedRegex(@"\b(20\d{2})\b")]
    private static partial Regex RegexAno();

    [GeneratedRegex(@"\b(\d{1,2})\b")]
    private static partial Regex RegexDia();

    public IReadOnlyList<Periodo> Importar(string caminho)
    {
        using var stream = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);

        var ano = DescobrirAno(wb) ?? _anoPadrao();
        var semanas = new List<Periodo>();

        foreach (var ws in wb.Worksheets)
        {
            var semana = LerSemana(ws, ano);
            if (semana is not null) semanas.Add(semana);
        }

        return semanas
            .GroupBy(s => (s.Inicio.Year, s.Inicio.Month))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var inicio = new DateOnly(g.Key.Year, g.Key.Month, 1);
                var mes = new Periodo
                {
                    Tipo = TipoPeriodo.Mes,
                    Titulo = Datas.NomeDoMes(g.Key.Year, g.Key.Month),
                    Inicio = inicio,
                    Fim = inicio.AddMonths(1).AddDays(-1)
                };
                foreach (var s in g.OrderBy(s => s.Inicio))
                {
                    s.PeriodoPai = mes;
                    s.PeriodoPaiId = mes.Id;
                    mes.Filhos.Add(s);
                }
                return mes;
            })
            .ToList();
    }

    private static int? DescobrirAno(XLWorkbook wb)
    {
        foreach (var ws in wb.Worksheets)
        {
            var m = RegexAno().Match(Texto(ws.Cell(1, 1)));
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return null;
    }

    private static Periodo? LerSemana(IXLWorksheet ws, int ano)
    {
        var titulo = Texto(ws.Cell(1, 1));
        var m = RegexIntervalo().Match(titulo);
        if (!m.Success) return null;

        var diaFim = int.Parse(m.Groups[3].Value);
        var mesFim = int.Parse(m.Groups[4].Value);
        var diaIni = int.Parse(m.Groups[1].Value);
        var mesIni = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : mesFim;
        var anoIni = mesIni > mesFim ? ano - 1 : ano;

        DateOnly inicio, fim;
        try
        {
            inicio = new DateOnly(anoIni, mesIni, diaIni);
            fim = new DateOnly(ano, mesFim, diaFim);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        if (fim < inicio) return null;

        var objetivo = Texto(ws.Cell(2, 1));
        if (objetivo.StartsWith("Objetivo:", StringComparison.OrdinalIgnoreCase))
            objetivo = objetivo["Objetivo:".Length..].Trim();

        var semana = new Periodo
        {
            Tipo = TipoPeriodo.Semana,
            Titulo = titulo,
            Objetivo = string.IsNullOrWhiteSpace(objetivo) ? null : objetivo,
            Inicio = inicio,
            Fim = fim
        };

        LerTarefas(ws, semana);
        return semana;
    }

    private static void LerTarefas(IXLWorksheet ws, Periodo semana)
    {
        var ultimaLinha = ws.LastRowUsed()?.RowNumber() ?? 0;
        var ultimaColuna = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Procura o cabeçalho ("Tipo" na primeira coluna).
        var linhaCabecalho = Enumerable.Range(1, Math.Min(ultimaLinha, 30))
            .FirstOrDefault(r => Texto(ws.Cell(r, 1)).Equals("Tipo", StringComparison.OrdinalIgnoreCase));
        if (linhaCabecalho == 0) return;

        var colunas = Enumerable.Range(1, ultimaColuna)
            .ToDictionary(c => Texto(ws.Cell(linhaCabecalho, c)).ToLowerInvariant(), c => c);
        int Col(string nome, int padrao) => colunas.TryGetValue(nome, out var c) ? c : padrao;

        int cTipo = Col("tipo", 1), cTarefa = Col("tarefa", 2), cDia = Col("dia", 3),
            cFeito = Col("feito", 4), cNotas = Col("notas", 6);

        Tarefa? principal = null;
        var ordem = 0;

        for (var r = linhaCabecalho + 1; r <= ultimaLinha; r++)
        {
            var tipo = Texto(ws.Cell(r, cTipo));
            var tituloTarefa = Texto(ws.Cell(r, cTarefa));
            if (tituloTarefa.Length == 0) continue;

            var (data, observacaoDia) = LerDia(Texto(ws.Cell(r, cDia)), semana);
            var notas = Juntar(observacaoDia, Texto(ws.Cell(r, cNotas)));
            var feitoCel = ws.Cell(r, cFeito);
            var feito = !feitoCel.HasFormula && EhFeito(Texto(feitoCel));

            if (tipo.Equals("Principal", StringComparison.OrdinalIgnoreCase))
            {
                principal = new Tarefa
                {
                    Titulo = tituloTarefa,
                    Data = data,
                    Notas = notas,
                    Ordem = ordem++,
                    Concluida = feito,
                    PeriodoId = semana.Id,
                    Periodo = semana
                };
                semana.Tarefas.Add(principal);
            }
            else if (tipo.Equals("Sub", StringComparison.OrdinalIgnoreCase) && principal is not null)
            {
                var sub = principal.AdicionarSubtarefa(tituloTarefa, data, notas);
                sub.Concluida = feito;
                semana.Tarefas.Add(sub);
            }
        }

        foreach (var t in semana.TarefasPrincipais) t.RecalcularConclusao();
    }

    /// <summary>"Seg 16" → data dentro da semana. "Ter–Qui" → sem data, vai para as notas.</summary>
    private static (DateOnly? Data, string? Observacao) LerDia(string dia, Periodo semana)
    {
        if (dia.Length == 0 || dia is "—" or "-" or "–") return (null, null);

        var m = RegexDia().Match(dia);
        if (m.Success)
        {
            var numero = int.Parse(m.Groups[1].Value);
            for (var d = semana.Inicio; d <= semana.Fim; d = d.AddDays(1))
                if (d.Day == numero) return (d, null);
        }
        return (null, $"Dias: {dia}");
    }

    private static bool EhFeito(string valor) =>
        valor is "✔" or "✓" or "✅" or "x" or "X" ||
        valor.Equals("sim", StringComparison.OrdinalIgnoreCase) ||
        valor.Equals("feito", StringComparison.OrdinalIgnoreCase);

    private static string? Juntar(params string?[] partes)
    {
        var validas = partes.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return validas.Length == 0 ? null : string.Join(" · ", validas);
    }

    private static string Texto(IXLCell cell)
    {
        try
        {
            return (cell.HasFormula ? cell.CachedValue.ToString() : cell.GetString()).Trim();
        }
        catch
        {
            return "";
        }
    }
}
