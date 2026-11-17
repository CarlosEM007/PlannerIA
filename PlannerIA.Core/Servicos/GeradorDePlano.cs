using System.Text;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;

namespace PlannerIA.Core.Servicos;

/// <summary>
/// Monta o pedido para a IA (data de hoje, semanas existentes e regras), chama o cliente
/// e valida a resposta. Devolve uma proposta; quem decide salvar é o usuário.
/// </summary>
public class GeradorDePlano(IClienteIa cliente, PlanoService plano)
{
    /// <summary>Instruções fixas (não mudam entre pedidos, o que favorece o cache de prompt).</summary>
    public const string Instrucoes = """
        Você é o assistente de planejamento do PlannerIA, um planner pessoal de rotina em português do Brasil.
        A partir do pedido do usuário, proponha semanas com objetivo e tarefas.

        Regras do plano:
        - Cada semana tem título, objetivo curto, data de início e fim (formato yyyy-MM-dd). Prefira semanas de segunda a domingo.
        - Título no formato "Semana N — 16 a 22/11" (ou "Semana N — 30/11 a 06/12" quando atravessa dois meses).
        - Cada semana tem tarefas principais; cada tarefa principal pode ter subtarefas. Só existe um nível de subtarefa.
        - Datas de tarefas e subtarefas precisam estar dentro da semana. Use null quando a tarefa não tem dia fixo
          (ex.: hábitos de vários dias) e explique os dias em "notas".
        - Para acrescentar tarefas a uma semana que já existe, repita exatamente as mesmas datas de início e fim dela
          e devolva só as tarefas novas. Não repita tarefas que já existem.
        - Não crie semanas no passado, a menos que o usuário peça.
        - Seja concreto e realista: tarefas pequenas, verbos no infinitivo, carga compatível com a rotina descrita.
        - Em "resumo", explique em 1 a 3 frases o que você propôs.
        """;

    public async Task<PlanoProposto> GerarAsync(string pedidoUsuario, DateOnly hoje, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pedidoUsuario))
            throw new RegraDeDominioException("Descreva o que você quer planejar.");

        var json = await cliente.GerarPlanoJsonAsync(Instrucoes, MontarPedido(pedidoUsuario, hoje), ct);
        var proposta = LeitorPlanoIa.Ler(json);

        foreach (var s in proposta.Semanas)
        {
            var sobreposta = plano.Semanas.FirstOrDefault(e =>
                !(e.Inicio == s.Inicio && e.Fim == s.Fim) && e.Inicio <= s.Fim && s.Inicio <= e.Fim);
            if (sobreposta is not null)
                proposta.Avisos.Add($"“{s.Titulo}” se sobrepõe a “{sobreposta.Titulo}”.");
        }
        return proposta;
    }

    public string MontarPedido(string pedidoUsuario, DateOnly hoje)
    {
        var sb = new StringBuilder();
        var diaSemana = hoje.ToDateTime(TimeOnly.MinValue).ToString("dddd", Datas.PtBr);
        sb.AppendLine($"Hoje: {diaSemana}, {hoje:yyyy-MM-dd} (fuso America/Sao_Paulo).");
        sb.AppendLine();

        var semanas = plano.Semanas.Where(s => s.Fim >= hoje.AddDays(-7)).ToList();
        if (semanas.Count == 0)
        {
            sb.AppendLine("Semanas já cadastradas a partir de hoje: nenhuma.");
        }
        else
        {
            sb.AppendLine("Semanas já cadastradas (não duplique; para acrescentar tarefas use as mesmas datas):");
            foreach (var s in semanas)
            {
                sb.AppendLine($"- {s.Titulo} | {s.Inicio:yyyy-MM-dd} a {s.Fim:yyyy-MM-dd} | objetivo: {s.Objetivo ?? "(sem objetivo)"}");
                foreach (var t in s.TarefasPrincipais)
                {
                    var p = t.Progresso();
                    var subs = t.TemSubtarefas ? $" — subtarefas: {string.Join("; ", t.SubtarefasOrdenadas.Select(Descrever))}" : "";
                    sb.AppendLine($"  • {t.Titulo} ({p} feitas){subs}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Pedido do usuário:");
        sb.AppendLine(pedidoUsuario.Trim());
        return sb.ToString();
    }

    private static string Descrever(Tarefa t) =>
        $"{t.Titulo}{(t.Data is { } d ? $" [{d:yyyy-MM-dd}]" : "")}{(t.Concluida ? " ✔" : "")}";
}
