using PlannerIA.Core.Portas;
using PlannerIA.Core.Servicos;

namespace PlannerIA.Servicos;

/// <summary>
/// Modo demonstração (PLANNERIA_IA_DEMO=1): simula a IA com um plano fixo, sem internet e sem custo.
/// Serve para testar a tela de geração e a prévia.
/// </summary>
public sealed class ClienteIaDemo : IClienteIa
{
    public string Provedor => "demonstração";

    public Task<EstadoIa> VerificarAsync(CancellationToken ct = default) => Task.FromResult(EstadoIa.Disponivel);

    public async Task<string> GerarPlanoJsonAsync(string instrucoes, string pedido, CancellationToken ct = default)
    {
        await Task.Delay(1800, ct);
        var seg = Datas.SegundaDaSemana(Datas.Hoje);
        string D(DateOnly d) => d.ToString("yyyy-MM-dd");
        var prox = seg.AddDays(7);

        return $$"""
            {
              "resumo": "Modo demonstração: acrescentei inglês diário à semana atual e montei a próxima semana com foco em portfólio.",
              "semanas": [
                { "titulo": "Semana atual", "objetivo": "", "inicio": "{{D(seg)}}", "fim": "{{D(seg.AddDays(6))}}",
                  "tarefas": [
                    { "titulo": "Inglês", "data": null, "notas": null, "subtarefas": [
                      { "titulo": "20 min de listening", "data": "{{D(seg.AddDays(1))}}", "notas": null },
                      { "titulo": "20 min de leitura", "data": "{{D(seg.AddDays(3))}}", "notas": null } ] } ] },
                { "titulo": "Semana {{prox:dd}} a {{prox.AddDays(6):dd/MM}}", "objetivo": "Portfólio e CV prontos para enviar", "inicio": "{{D(prox)}}", "fim": "{{D(prox.AddDays(6))}}",
                  "tarefas": [
                    { "titulo": "Portfólio no itch.io", "data": null, "notas": null, "subtarefas": [
                      { "titulo": "Organizar os jogos publicados", "data": "{{D(prox)}}", "notas": null },
                      { "titulo": "GIF e descrição de cada jogo", "data": "{{D(prox.AddDays(2))}}", "notas": null } ] },
                    { "titulo": "CV de game dev", "data": null, "notas": null, "subtarefas": [
                      { "titulo": "Rascunho em inglês", "data": "{{D(prox.AddDays(1))}}", "notas": null },
                      { "titulo": "Revisar com o professor", "data": "{{D(prox.AddDays(4))}}", "notas": null } ] },
                    { "titulo": "Leitura de arquitetura .NET", "data": null, "notas": "3 sessões de 30 min", "subtarefas": [] } ] }
              ]
            }
            """;
    }
}
