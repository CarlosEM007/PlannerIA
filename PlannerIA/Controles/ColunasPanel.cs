using System.Windows;
using System.Windows.Controls;

namespace PlannerIA.Controles;

/// <summary>
/// Painel em colunas estilo "masonry": o número de colunas depende da largura disponível
/// e cada item entra na coluna mais curta.
/// </summary>
public class ColunasPanel : Panel
{
    public static readonly DependencyProperty LarguraMinimaColunaProperty = DependencyProperty.Register(
        nameof(LarguraMinimaColuna), typeof(double), typeof(ColunasPanel),
        new FrameworkPropertyMetadata(340d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty EspacamentoProperty = DependencyProperty.Register(
        nameof(Espacamento), typeof(double), typeof(ColunasPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaximoColunasProperty = DependencyProperty.Register(
        nameof(MaximoColunas), typeof(int), typeof(ColunasPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double LarguraMinimaColuna { get => (double)GetValue(LarguraMinimaColunaProperty); set => SetValue(LarguraMinimaColunaProperty, value); }
    public double Espacamento { get => (double)GetValue(EspacamentoProperty); set => SetValue(EspacamentoProperty, value); }
    public int MaximoColunas { get => (int)GetValue(MaximoColunasProperty); set => SetValue(MaximoColunasProperty, value); }

    private readonly List<Rect> _posicoes = [];

    private (int Colunas, double Largura) Layout(double larguraDisponivel)
    {
        if (double.IsInfinity(larguraDisponivel)) larguraDisponivel = LarguraMinimaColuna * 2;
        var cols = (int)Math.Floor((larguraDisponivel + Espacamento) / (LarguraMinimaColuna + Espacamento));
        cols = Math.Clamp(cols, 1, Math.Max(1, MaximoColunas));
        var largura = (larguraDisponivel - Espacamento * (cols - 1)) / cols;
        return (cols, Math.Max(0, largura));
    }

    protected override Size MeasureOverride(Size disponivel)
    {
        var (cols, largura) = Layout(disponivel.Width);
        var alturas = new double[cols];
        _posicoes.Clear();

        foreach (UIElement filho in InternalChildren)
        {
            filho.Measure(new Size(largura, double.PositiveInfinity));
            var col = Array.IndexOf(alturas, alturas.Min());
            var y = alturas[col];
            _posicoes.Add(new Rect(col * (largura + Espacamento), y, largura, filho.DesiredSize.Height));
            alturas[col] += filho.DesiredSize.Height + Espacamento;
        }

        var altura = InternalChildren.Count == 0 ? 0 : Math.Max(0, alturas.Max() - Espacamento);
        var larguraTotal = double.IsInfinity(disponivel.Width) ? cols * largura + (cols - 1) * Espacamento : disponivel.Width;
        return new Size(larguraTotal, altura);
    }

    protected override Size ArrangeOverride(Size final)
    {
        // Recalcula para a largura final (pode diferir da medida).
        MeasureOverride(final);
        for (var i = 0; i < InternalChildren.Count && i < _posicoes.Count; i++)
            InternalChildren[i].Arrange(_posicoes[i]);
        return final;
    }
}
