using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PlannerIA.Controles;

/// <summary>Anel de progresso com animação.</summary>
public class AnelProgresso : FrameworkElement
{
    public static readonly DependencyProperty ValorProperty = DependencyProperty.Register(
        nameof(Valor), typeof(double), typeof(AnelProgresso),
        new PropertyMetadata(0d, (d, e) => ((AnelProgresso)d).Animar((double)e.NewValue)));

    private static readonly DependencyProperty ValorAnimadoProperty = DependencyProperty.Register(
        "ValorAnimado", typeof(double), typeof(AnelProgresso),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EspessuraProperty = DependencyProperty.Register(
        nameof(Espessura), typeof(double), typeof(AnelProgresso),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrilhoProperty = DependencyProperty.Register(
        nameof(Trilho), typeof(Brush), typeof(AnelProgresso),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PreenchimentoProperty = DependencyProperty.Register(
        nameof(Preenchimento), typeof(Brush), typeof(AnelProgresso),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Valor { get => (double)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public double Espessura { get => (double)GetValue(EspessuraProperty); set => SetValue(EspessuraProperty, value); }
    public Brush Trilho { get => (Brush)GetValue(TrilhoProperty); set => SetValue(TrilhoProperty, value); }
    public Brush Preenchimento { get => (Brush)GetValue(PreenchimentoProperty); set => SetValue(PreenchimentoProperty, value); }

    public AnelProgresso()
    {
        Loaded += (_, _) => Animar(Valor, inicial: true);
    }

    private void Animar(double alvo, bool inicial = false)
    {
        alvo = Math.Clamp(double.IsNaN(alvo) ? 0 : alvo, 0, 1);
        if (!IsLoaded && !inicial) { SetValue(ValorAnimadoProperty, alvo); return; }
        if (inicial) SetValue(ValorAnimadoProperty, 0d);

        var anim = new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(inicial ? 1100 : 600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(ValorAnimadoProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var tamanho = Math.Min(ActualWidth, ActualHeight);
        if (tamanho <= 0) return;

        var e = Espessura;
        var raio = (tamanho - e) / 2;
        var centro = new Point(ActualWidth / 2, ActualHeight / 2);

        var penTrilho = new Pen(Trilho, e);
        dc.DrawEllipse(null, penTrilho, centro, raio, raio);

        var v = (double)GetValue(ValorAnimadoProperty);
        if (v <= 0.0001) return;

        var pen = new Pen(Preenchimento, e) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.9999)
        {
            dc.DrawEllipse(null, pen, centro, raio, raio);
            return;
        }

        var angulo = v * 360;
        var inicio = PontoNoArco(centro, raio, 0);
        var fim = PontoNoArco(centro, raio, angulo);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(inicio, false, false);
            ctx.ArcTo(fim, new Size(raio, raio), 0, angulo > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point PontoNoArco(Point c, double r, double graus)
    {
        var rad = (graus - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
