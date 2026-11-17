using Microsoft.EntityFrameworkCore;
using PlannerIA.Core.Dominio;

namespace PlannerIA.Infrastructure.Dados;

public class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options)
{
    public DbSet<Periodo> Periodos => Set<Periodo>();
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Periodo>(e =>
        {
            e.ToTable("Periodos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Titulo).IsRequired().HasMaxLength(200);
            e.Property(p => p.Objetivo).HasMaxLength(1000);
            e.Property(p => p.Tipo).HasConversion<int>();
            e.HasIndex(p => new { p.Tipo, p.Inicio });

            e.HasOne(p => p.PeriodoPai)
                .WithMany(p => p.Filhos)
                .HasForeignKey(p => p.PeriodoPaiId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(p => p.TarefasPrincipais);
            e.Ignore(p => p.Dias);
        });

        mb.Entity<Tarefa>(e =>
        {
            e.ToTable("Tarefas");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Titulo).IsRequired().HasMaxLength(300);
            e.Property(t => t.Notas).HasMaxLength(2000);
            e.HasIndex(t => t.Data);

            e.HasOne(t => t.Periodo)
                .WithMany(p => p.Tarefas)
                .HasForeignKey(t => t.PeriodoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.TarefaPai)
                .WithMany(t => t.Subtarefas)
                .HasForeignKey(t => t.TarefaPaiId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(t => t.EhSubtarefa);
            e.Ignore(t => t.TemSubtarefas);
            e.Ignore(t => t.PodeMarcarManualmente);
            e.Ignore(t => t.SubtarefasOrdenadas);
        });
    }
}
