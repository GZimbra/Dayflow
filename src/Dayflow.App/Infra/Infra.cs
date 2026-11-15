using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.Infra;

/// <summary>Mensagem do WeakReferenceMessenger: algo mudou no banco, telas recarregam.</summary>
public sealed record DadosAlterados(string Origem);

public interface IDialogos
{
    Task AlertaAsync(string titulo, string mensagem);
    Task<bool> ConfirmarAsync(string titulo, string mensagem, string aceitar = "Confirmar", string cancelar = "Cancelar");
    Task<string?> EscolherAsync(string titulo, string? destrutiva, params string[] opcoes);
}

public sealed class Dialogos : IDialogos
{
    private static Page Pagina =>
        Application.Current?.Windows.FirstOrDefault()?.Page is { } p
            ? (p.Navigation.ModalStack.LastOrDefault() ?? (p as Shell)?.CurrentPage ?? p)
            : throw new InvalidOperationException("Sem janela ativa.");

    public Task AlertaAsync(string titulo, string mensagem) =>
        MainThread.InvokeOnMainThreadAsync(() => Pagina.DisplayAlertAsync(titulo, mensagem, "OK"));

    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string aceitar = "Confirmar", string cancelar = "Cancelar") =>
        MainThread.InvokeOnMainThreadAsync(() => Pagina.DisplayAlertAsync(titulo, mensagem, aceitar, cancelar));

    public async Task<string?> EscolherAsync(string titulo, string? destrutiva, params string[] opcoes)
    {
        var r = await MainThread.InvokeOnMainThreadAsync(() => Pagina.DisplayActionSheetAsync(titulo, "Cancelar", destrutiva, opcoes));
        return r is null or "Cancelar" ? null : r;
    }
}

/// <summary>Abre telas modais resolvendo página + ViewModel pelo DI.</summary>
public interface INavegacao
{
    Task AbrirAsync<TPagina, TVm>(Func<TVm, Task> inicializar) where TPagina : Page where TVm : class;
    Task FecharAsync();
}

public sealed class Navegacao(IServiceProvider sp) : INavegacao
{
    public async Task AbrirAsync<TPagina, TVm>(Func<TVm, Task> inicializar) where TPagina : Page where TVm : class
    {
        var pagina = sp.GetRequiredService<TPagina>();
        var vm = (TVm)pagina.BindingContext;
        await inicializar(vm);
        await Shell.Current.Navigation.PushModalAsync(pagina, animated: true);
    }

    public Task FecharAsync() =>
        Shell.Current.Navigation.ModalStack.Count > 0 ? Shell.Current.Navigation.PopModalAsync(true) : Task.CompletedTask;
}

/// <summary>Preferências locais (SharedPreferences). allowBackup=false: não vão para a nuvem.</summary>
public interface IConfiguracoes
{
    AppTheme Tema { get; set; }
    ConfiguracaoLembretes Lembretes { get; set; }
    OrientacaoTela Orientacao { get; set; }
}

public enum OrientacaoTela { Automatica = 0, Paisagem = 1, Retrato = 2 }

/// <summary>Aplica a orientação escolhida em Ajustes à Activity atual (Android).</summary>
public static class Orientacao
{
    public const string Chave = "orientacao";

    public static void Aplicar(OrientacaoTela o)
    {
#if ANDROID
        if (Platform.CurrentActivity is { } a) a.RequestedOrientation = ParaAndroid(o);
#endif
    }

#if ANDROID
    /// <summary>Automática = sensor completo (gira mesmo com a rotação travada no sistema, como pedido: usar deitado ou em pé).</summary>
    public static Android.Content.PM.ScreenOrientation ParaAndroid(OrientacaoTela o) => o switch
    {
        OrientacaoTela.Paisagem => Android.Content.PM.ScreenOrientation.SensorLandscape,
        OrientacaoTela.Retrato => Android.Content.PM.ScreenOrientation.SensorPortrait,
        _ => Android.Content.PM.ScreenOrientation.FullSensor,
    };
#endif
}

public sealed class Configuracoes : IConfiguracoes
{
    private readonly IPreferences _p = Preferences.Default;

    public AppTheme Tema
    {
        get => (AppTheme)_p.Get("tema", (int)AppTheme.Unspecified);
        set => _p.Set("tema", (int)value);
    }

    public OrientacaoTela Orientacao
    {
        get => (OrientacaoTela)_p.Get(Dayflow.App.Infra.Orientacao.Chave, 0);
        set => _p.Set(Dayflow.App.Infra.Orientacao.Chave, (int)value);
    }

    public ConfiguracaoLembretes Lembretes
    {
        get
        {
            var silenciado = _p.Get("lemb_silenciado", 0);
            return new ConfiguracaoLembretes(
                _p.Get("lemb_ativo", false),
                _p.Get("lemb_ini", 8),
                _p.Get("lemb_fim", 18),
                (DiasSemana)_p.Get("lemb_dias", (int)DiasSemana.Uteis),
                silenciado > 0 ? DateOnly.FromDayNumber(silenciado) : null);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _p.Set("lemb_ativo", value.Ativo);
            _p.Set("lemb_ini", value.HoraInicio);
            _p.Set("lemb_fim", value.HoraFim);
            _p.Set("lemb_dias", (int)value.Dias);
            _p.Set("lemb_silenciado", value.SilenciadoAte?.DayNumber ?? 0);
        }
    }
}

/// <summary>Única saída de dados do app: arquivo no cache + Intent de compartilhamento escolhida pelo usuário.</summary>
public interface IExportador
{
    Task CompartilharAsync(string nomeArquivo, string conteudo, string mime, string titulo);
    Task CompartilharAsync(string nomeArquivo, byte[] conteudo, string mime, string titulo);
}

public sealed class Exportador : IExportador
{
    public async Task CompartilharAsync(string nomeArquivo, string conteudo, string mime, string titulo)
    {
        var caminho = Preparar(nomeArquivo); // não acumula cópias
        await File.WriteAllTextAsync(caminho, conteudo, new System.Text.UTF8Encoding(false));
        await Share.Default.RequestAsync(new ShareFileRequest { Title = titulo, File = new ShareFile(caminho, mime) });
    }

    /// <summary>Binário (PNG). Mesmo fluxo: arquivo no cache privado + Intent de compartilhamento disparado pelo usuário.</summary>
    public async Task CompartilharAsync(string nomeArquivo, byte[] conteudo, string mime, string titulo)
    {
        var caminho = Preparar(nomeArquivo);
        await File.WriteAllBytesAsync(caminho, conteudo);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = titulo, File = new ShareFile(caminho, mime) });
    }

    private static string Preparar(string nomeArquivo)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "export");
        Directory.CreateDirectory(dir);
        foreach (var antigo in Directory.EnumerateFiles(dir)) File.Delete(antigo);
        return Path.Combine(dir, Path.GetFileName(nomeArquivo));
    }
}

public static class Fmt
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public static string DiaExtenso(DateOnly d) => d.ToString("d 'de' MMMM", PtBr);
    public static string DiaSemana(DateOnly d) => d.ToString("dddd", PtBr);
    public static string MesAno(DateOnly d) => PtBr.TextInfo.ToTitleCase(d.ToString("MMMM yyyy", PtBr));
    public static string Curto(DateOnly d) => d.ToString("dd/MM", PtBr);
    public static string Duracao(int minutos) => minutos < 60 ? $"{minutos} min" : $"{minutos / 60}h{minutos % 60:00}";
    public static string Relogio(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    public static Color Cor(string? hex, string padrao = "#9AA3AE") => Color.FromArgb(string.IsNullOrEmpty(hex) ? padrao : hex);

    private static readonly (DiasSemana Bit, string Sigla)[] Siglas =
    [
        (Dayflow.Core.Domain.DiasSemana.Segunda, "Seg"), (Dayflow.Core.Domain.DiasSemana.Terca, "Ter"),
        (Dayflow.Core.Domain.DiasSemana.Quarta, "Qua"), (Dayflow.Core.Domain.DiasSemana.Quinta, "Qui"),
        (Dayflow.Core.Domain.DiasSemana.Sexta, "Sex"), (Dayflow.Core.Domain.DiasSemana.Sabado, "Sáb"),
        (Dayflow.Core.Domain.DiasSemana.Domingo, "Dom"),
    ];

    public static IReadOnlyList<(DiasSemana Bit, string Sigla)> DiasDaSemana => Siglas;

    public static string Dias(DiasSemana d) => d switch
    {
        Dayflow.Core.Domain.DiasSemana.Todos => "Todos os dias",
        Dayflow.Core.Domain.DiasSemana.Uteis => "Dias úteis",
        _ => string.Join(" · ", Siglas.Where(s => (d & s.Bit) != 0).Select(s => s.Sigla)),
    };
}

/// <summary>ObservableCollection com troca em lote: uma única notificação Reset para a UI.</summary>
public sealed class ColecaoObservavel<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
    public ColecaoObservavel() { }

    public void Redefinir(IList<T> itens)
    {
        ArgumentNullException.ThrowIfNull(itens);
        if (Items.Count == 0 && itens.Count == 0) return;
        CheckReentrancy();
        Items.Clear();
        foreach (var i in itens) Items.Add(i);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Base dos ViewModels: estado Ocupado e tratamento uniforme de erro.</summary>
public abstract partial class VmBase(IDialogos dialogos, ILogger logger) : ObservableObject
{
    protected IDialogos Dialogos { get; } = dialogos;
    protected ILogger Log { get; } = logger;

    [ObservableProperty] private bool _ocupado;

    // Cargas concorrentes (OnAppearing + mensagem DadosAlterados + troca de filtro) não podem
    // intercalar Clear/Add: só a carga mais recente aplica o resultado ("a última vence").
    private readonly Dictionary<string, int> _geracoes = [];

    protected int NovaGeracao(string chave = "")
    {
        var g = _geracoes.GetValueOrDefault(chave) + 1;
        _geracoes[chave] = g;
        return g;
    }

    protected bool EhAtual(int geracao, string chave = "") => _geracoes.GetValueOrDefault(chave) == geracao;

    /// <summary>Troca o conteúdo da coleção de uma vez, na thread de UI, depois que os dados já foram buscados.
    /// Em <see cref="ColecaoObservavel{T}"/> gera UMA notificação (Reset) em vez de Clear + N Adds.</summary>
    protected static void Substituir<T>(System.Collections.ObjectModel.ObservableCollection<T> alvo, IEnumerable<T> itens)
    {
        ArgumentNullException.ThrowIfNull(alvo);
        var lista = itens.ToList();
        if (alvo is ColecaoObservavel<T> c) { c.Redefinir(lista); return; }
        alvo.Clear();
        foreach (var i in lista) alvo.Add(i);
    }

    // ===== Recarga sob demanda =====
    // Antes: cada alteração (concluir, cronômetro, editar) recarregava TODAS as telas, visíveis ou não,
    // e cada troca de aba recarregava de novo. Agora: tela invisível só marca "suja"; tela visível
    // recarrega uma vez por rajada de alterações (coalescida).
    private bool _visivel;
    private bool _sujo = true;
    private bool _recargaAgendada;

    /// <summary>Chamar no OnAppearing. Retorna true se a tela precisa carregar dados.</summary>
    public bool AoAparecer()
    {
        _visivel = true;
        var precisa = _sujo;
        _sujo = false;
        return precisa;
    }

    public void AoSumir() => _visivel = false;

    /// <summary>Dados mudaram em algum lugar do app.</summary>
    protected void Invalidar(Func<Task> recarregar)
    {
        if (!_visivel) { _sujo = true; return; }
        if (_recargaAgendada) return;
        _recargaAgendada = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(60); // junta rajadas (ex.: concluir = pausar + salvar + relatório)
            _recargaAgendada = false;
            if (_visivel) await recarregar();
            else _sujo = true;
        });
    }

    protected async Task ExecutarAsync(Func<Task> acao)
    {
        try
        {
            Ocupado = true;
            await acao();
        }
        catch (RegraDeNegocioException ex)
        {
            // Mensagem de regra é segura (sem dado pessoal); log só o código.
            Log.LogInformation("Regra violada: {Codigo}", ex.Codigo);
            await Dialogos.AlertaAsync("Atenção", ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.LogError(ex, "Falha inesperada em {Vm}", GetType().Name);
            try
            {
                await Dialogos.AlertaAsync("Erro", "Algo deu errado. O detalhe técnico foi gravado no log local (Ajustes → Exportar log).");
            }
            catch (Exception alerta) when (alerta is InvalidOperationException or NullReferenceException or ObjectDisposedException)
            {
                // sem janela/página ativa (ex.: tela trocando ao girar): já está no log, não derruba o app
                Log.LogWarning("Alerta de erro não exibido: {Tipo}", alerta.GetType().Name);
            }
        }
        finally
        {
            Ocupado = false;
        }
    }
}
