using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;

namespace Dayflow.App.ViewModels;

public enum ModoPeriodo { Dia, Semana, Mes, Personalizado }

/// <summary>Estado de período compartilhado por Relatórios e Gráficos.</summary>
public sealed partial class SeletorPeriodo : ObservableObject
{
    private readonly IRelogioCivil _relogio;

    public SeletorPeriodo(IRelogioCivil relogio, ModoPeriodo modo)
    {
        _relogio = relogio;
        Referencia = relogio.Hoje;
        Modo = modo;
        var p = Periodo.Mes(Referencia);
        Inicio = p.Inicio.ToDateTime(TimeOnly.MinValue);
        Fim = p.Fim.ToDateTime(TimeOnly.MinValue);
    }

    public event EventHandler? Mudou;

    [ObservableProperty] private ModoPeriodo _modo;
    [ObservableProperty] private DateOnly _referencia;
    [ObservableProperty] private DateTime _inicio;
    [ObservableProperty] private DateTime _fim;

    public bool EhDia => Modo == ModoPeriodo.Dia;
    public bool EhSemana => Modo == ModoPeriodo.Semana;
    public bool EhMes => Modo == ModoPeriodo.Mes;
    public bool EhPersonalizado => Modo == ModoPeriodo.Personalizado;
    public bool Navegavel => !EhPersonalizado;

    public Periodo Atual => Modo switch
    {
        ModoPeriodo.Dia => Periodo.DoDia(Referencia),
        ModoPeriodo.Semana => Periodo.Semana(Referencia),
        ModoPeriodo.Mes => Periodo.Mes(Referencia),
        _ => new Periodo(DateOnly.FromDateTime(Inicio), DateOnly.FromDateTime(Fim)),
    };

    public string Rotulo
    {
        get
        {
            try
            {
                var p = Atual;
                return Modo switch
                {
                    ModoPeriodo.Dia => $"{Fmt.DiaSemana(p.Inicio)}, {Fmt.DiaExtenso(p.Inicio)}",
                    ModoPeriodo.Mes => Fmt.MesAno(p.Inicio),
                    _ => $"{Fmt.Curto(p.Inicio)} – {Fmt.Curto(p.Fim)}/{p.Fim.Year}",
                };
            }
            catch (RegraDeNegocioException ex) { return ex.Message; }
        }
    }

    partial void OnModoChanged(ModoPeriodo value) => Notificar();
    partial void OnReferenciaChanged(DateOnly value) => Notificar();
    partial void OnInicioChanged(DateTime value) { if (EhPersonalizado) Notificar(); }
    partial void OnFimChanged(DateTime value) { if (EhPersonalizado) Notificar(); }

    [RelayCommand]
    private void DefinirModo(string? modo)
    {
        if (Enum.TryParse<ModoPeriodo>(modo, out var m)) Modo = m;
    }

    [RelayCommand]
    private void Anterior() => Deslocar(-1);

    [RelayCommand]
    private void Seguinte() => Deslocar(1);

    [RelayCommand]
    private void Hoje() => Referencia = _relogio.Hoje;

    private void Deslocar(int s) => Referencia = Modo switch
    {
        ModoPeriodo.Dia => Referencia.AddDays(s),
        ModoPeriodo.Semana => Referencia.AddDays(7 * s),
        _ => Referencia.AddMonths(s),
    };

    private void Notificar()
    {
        foreach (var n in new[] { nameof(EhDia), nameof(EhSemana), nameof(EhMes), nameof(EhPersonalizado), nameof(Navegavel), nameof(Rotulo) })
            OnPropertyChanged(n);
        Mudou?.Invoke(this, EventArgs.Empty);
    }
}
