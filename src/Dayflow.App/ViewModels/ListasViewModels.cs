using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Infra;
using Dayflow.App.Views;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed class RotinaItemVm(Rotina r, string? projeto, string? cor)
{
    public Rotina Rotina { get; } = r;
    public string Titulo => Rotina.Titulo;
    public string Dias => Fmt.Dias(Rotina.Dias);
    public string Projeto => projeto ?? Rotulos.SemProjeto;
    public Color Cor => Fmt.Cor(cor, "#C9C9C6");
    public bool Ativa => Rotina.Ativa;
    public double Opacidade => Ativa ? 1 : 0.5;
    public string Estado => Ativa ? "Ativa" : "Pausada";
}

public sealed partial class RotinasViewModel : VmBase, IRecipient<DadosAlterados>
{
    private readonly ServicoRotinas _rotinas;
    private readonly ServicoProjetos _projetos;
    private readonly INavegacao _nav;

    public RotinasViewModel(ServicoRotinas rotinas, ServicoProjetos projetos, INavegacao nav, IDialogos d, ILogger<RotinasViewModel> log)
        : base(d, log)
    {
        _rotinas = rotinas;
        _projetos = projetos;
        _nav = nav;
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<RotinaItemVm> Itens { get; } = new ColecaoObservavel<RotinaItemVm>();
    [ObservableProperty] private bool _vazia;

    public void Receive(DadosAlterados message) => Invalidar(CarregarAsync);

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var g = NovaGeracao();
        var proj = (await _projetos.ListarAsync(true)).ToDictionary(p => p.Id);
        var rotinas = await _rotinas.ListarAsync();
        if (!EhAtual(g)) return;
        Substituir(Itens, rotinas.Select(r =>
        {
            var p = r.ProjetoId is { } id ? proj.GetValueOrDefault(id) : null;
            return new RotinaItemVm(r, p?.Nome, p?.Cor);
        }));
        Vazia = Itens.Count == 0;
    });

    [RelayCommand]
    private Task NovaAsync() => _nav.AbrirAsync<EditorRotinaPage, EditorRotinaViewModel>(vm => vm.InicializarAsync(null));

    [RelayCommand]
    private Task EditarAsync(RotinaItemVm? i) => i is null ? Task.CompletedTask :
        _nav.AbrirAsync<EditorRotinaPage, EditorRotinaViewModel>(vm => vm.InicializarAsync(i.Rotina));

    [RelayCommand]
    private Task AlternarAtivaAsync(RotinaItemVm? i) => i is null ? Task.CompletedTask : ExecutarAsync(async () =>
    {
        await _rotinas.DefinirAtivaAsync(i.Rotina.Id, !i.Ativa);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("rotina"));
    });

    [RelayCommand]
    private async Task ExcluirAsync(RotinaItemVm? i)
    {
        if (i is null || !await Dialogos.ConfirmarAsync("Excluir rotina", "Só é possível excluir rotina sem histórico. Com histórico, pause-a.", "Excluir")) return;
        await ExecutarAsync(async () =>
        {
            await _rotinas.ExcluirAsync(i.Rotina.Id);
            WeakReferenceMessenger.Default.Send(new DadosAlterados("rotina"));
        });
    }
}

public sealed class ProjetoItemVm(Projeto p)
{
    public Projeto Projeto { get; } = p;
    public string Nome => Projeto.Nome;
    public Color Cor => Fmt.Cor(Projeto.Cor);
    public bool Ativo => Projeto.Ativo;
    public double Opacidade => Ativo ? 1 : 0.5;
    public string AcaoArquivo => Ativo ? "Arquivar" : "Reativar";
}

public sealed partial class ProjetosViewModel : VmBase, IRecipient<DadosAlterados>
{
    private readonly ServicoProjetos _projetos;
    private readonly INavegacao _nav;

    public ProjetosViewModel(ServicoProjetos projetos, INavegacao nav, IDialogos d, ILogger<ProjetosViewModel> log) : base(d, log)
    {
        _projetos = projetos;
        _nav = nav;
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<ProjetoItemVm> Itens { get; } = new ColecaoObservavel<ProjetoItemVm>();
    [ObservableProperty] private bool _mostrarArquivados;
    [ObservableProperty] private bool _vazia;

    partial void OnMostrarArquivadosChanged(bool value) => _ = CarregarAsync();

    public void Receive(DadosAlterados message) => Invalidar(CarregarAsync);

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var g = NovaGeracao();
        var lista = await _projetos.ListarAsync(MostrarArquivados);
        if (!EhAtual(g)) return;
        Substituir(Itens, lista.Select(p => new ProjetoItemVm(p)));
        Vazia = Itens.Count == 0;
    });

    [RelayCommand]
    private Task NovoAsync() => _nav.AbrirAsync<EditorProjetoPage, EditorProjetoViewModel>(vm => { vm.Inicializar(null); return Task.CompletedTask; });

    [RelayCommand]
    private Task EditarAsync(ProjetoItemVm? i) => i is null ? Task.CompletedTask :
        _nav.AbrirAsync<EditorProjetoPage, EditorProjetoViewModel>(vm => { vm.Inicializar(i.Projeto); return Task.CompletedTask; });

    [RelayCommand]
    private Task AlternarArquivoAsync(ProjetoItemVm? i) => i is null ? Task.CompletedTask : ExecutarAsync(async () =>
    {
        await _projetos.DefinirArquivadoAsync(i.Projeto.Id, i.Ativo);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("projeto"));
    });

    [RelayCommand]
    private async Task ExcluirAsync(ProjetoItemVm? i)
    {
        if (i is null || !await Dialogos.ConfirmarAsync("Excluir projeto", "Só projetos sem tarefas podem ser excluídos. Com histórico, arquive.", "Excluir")) return;
        await ExecutarAsync(async () =>
        {
            await _projetos.ExcluirAsync(i.Projeto.Id);
            WeakReferenceMessenger.Default.Send(new DadosAlterados("projeto"));
        });
    }
}
