using Microsoft.EntityFrameworkCore;
using PlannerIA.Core.Dominio;
using PlannerIA.Core.Portas;

namespace PlannerIA.Infrastructure.Dados;

/// <summary>Repositório SQLite com um DbContext de longa duração (app desktop de um usuário).</summary>
public sealed class PlanoRepository(PlannerDbContext db) : IPlanoRepository, IDisposable
{
    public async Task<List<Periodo>> CarregarMesesAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        // Carrega tudo; o EF liga as navegações (meses → semanas → tarefas → subtarefas).
        var periodos = await db.Periodos.ToListAsync(ct);
        await db.Tarefas.LoadAsync(ct);

        return periodos.Where(p => p.Tipo == TipoPeriodo.Mes).OrderBy(p => p.Inicio).ToList();
    }

    public void Adicionar(Periodo periodo) => db.Periodos.Add(periodo);
    public void Adicionar(Tarefa tarefa) => db.Tarefas.Add(tarefa);
    public void Remover(Periodo periodo) => db.Periodos.Remove(periodo);
    public void Remover(Tarefa tarefa) => db.Tarefas.Remove(tarefa);

    public Task SalvarAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public void Dispose() => db.Dispose();

    public static PlanoRepository CriarParaArquivo(string caminhoBanco)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminhoBanco)!);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseSqlite($"Data Source={caminhoBanco}")
            .Options;
        return new PlanoRepository(new PlannerDbContext(options));
    }
}
