using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PlannerIA.Servicos;

/// <summary>Pastas locais do app. Tudo fica no computador (offline).</summary>
public static class Caminhos
{
    /// <summary>%LOCALAPPDATA%\PlannerIA, ou a pasta em PLANNERIA_DATA_DIR (útil para testes).</summary>
    public static string PastaDados { get; } =
        Environment.GetEnvironmentVariable("PLANNERIA_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlannerIA");

    public static string Banco => Path.Combine(PastaDados, "planner.db");
    public static string Configuracao => Path.Combine(PastaDados, "config.json");
}

public class ConfiguracaoApp
{
    public string Tema { get; set; } = "Escuro";

    /// <summary>Automatico, ClaudeCli ou Api.</summary>
    public string ModoIa { get; set; } = "Automatico";

    public Core.Portas.ModoIa LerModoIa() =>
        Enum.TryParse<Core.Portas.ModoIa>(ModoIa, out var m) ? m : Core.Portas.ModoIa.Automatico;

    public static ConfiguracaoApp Carregar()
    {
        try
        {
            if (File.Exists(Caminhos.Configuracao))
                return JsonSerializer.Deserialize<ConfiguracaoApp>(File.ReadAllText(Caminhos.Configuracao)) ?? new();
        }
        catch
        {
            // Arquivo corrompido: volta ao padrão.
        }
        return new();
    }

    public void Salvar()
    {
        Directory.CreateDirectory(Caminhos.PastaDados);
        File.WriteAllText(Caminhos.Configuracao, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class GerenciadorTema
{
    public static string Atual { get; private set; } = "Escuro";

    public static void Aplicar(string tema)
    {
        Atual = tema == "Claro" ? "Claro" : "Escuro";
        var dicionarios = Application.Current.Resources.MergedDictionaries;
        var novo = new ResourceDictionary { Source = new Uri($"Temas/{Atual}.xaml", UriKind.Relative) };
        if (dicionarios.Count > 0) dicionarios[0] = novo;
        else dicionarios.Insert(0, novo);
    }
}

/// <summary>Cor estável por categoria (título da tarefa principal), igual em todas as semanas.</summary>
public static class Paleta
{
    private static readonly SolidColorBrush[] Cores =
    [
        Criar("#7C5CFF"), Criar("#22C3A6"), Criar("#F5A524"), Criar("#F0506E"),
        Criar("#3B9EFF"), Criar("#8BC34A"), Criar("#FF7A45"), Criar("#D46BFF")
    ];

    private static SolidColorBrush Criar(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static readonly Dictionary<string, SolidColorBrush> Atribuidas = [];

    private static string Chave(string titulo) => titulo.Trim().ToLowerInvariant();

    /// <summary>
    /// Distribui as cores pela ordem em que as categorias aparecem no plano, para que as primeiras
    /// categorias nunca repitam cor. Títulos iguais ("Inglês") mantêm a mesma cor em todas as semanas.
    /// </summary>
    public static void Definir(IEnumerable<string> titulosEmOrdem)
    {
        Atribuidas.Clear();
        foreach (var t in titulosEmOrdem.Where(t => !string.IsNullOrWhiteSpace(t)).Select(Chave).Distinct())
            Atribuidas[t] = Cores[Atribuidas.Count % Cores.Length];
    }

    public static SolidColorBrush CorPara(string? titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo)) return Cores[0];

        var chave = Chave(titulo);
        if (Atribuidas.TryGetValue(chave, out var cor)) return cor;

        uint hash = 2166136261;
        foreach (var c in chave) hash = (hash ^ c) * 16777619;
        return Cores[hash % (uint)Cores.Length];
    }
}
