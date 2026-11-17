using System.Globalization;

namespace PlannerIA.Core.Servicos;

public static class Datas
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public static string NomeDoMes(int ano, int mes)
    {
        var nome = PtBr.DateTimeFormat.GetMonthName(mes);
        return $"{char.ToUpper(nome[0], PtBr)}{nome[1..]} {ano}";
    }

    /// <summary>"16 a 22/11" ou "30/11 a 06/12".</summary>
    public static string Intervalo(DateOnly inicio, DateOnly fim) =>
        inicio.Month == fim.Month && inicio.Year == fim.Year
            ? $"{inicio:dd} a {fim:dd/MM}"
            : $"{inicio:dd/MM} a {fim:dd/MM}";

    /// <summary>"Seg 16".</summary>
    public static string DiaCurto(DateOnly data)
    {
        var dia = PtBr.DateTimeFormat.GetAbbreviatedDayName(data.DayOfWeek).TrimEnd('.');
        return $"{char.ToUpper(dia[0], PtBr)}{dia[1..]} {data:dd}";
    }

    public static DateOnly SegundaDaSemana(DateOnly data)
    {
        var delta = ((int)data.DayOfWeek + 6) % 7; // segunda = 0
        return data.AddDays(-delta);
    }
}
