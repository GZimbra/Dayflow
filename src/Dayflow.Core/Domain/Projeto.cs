using System.Text.RegularExpressions;

namespace Dayflow.Core.Domain;

public sealed partial class Projeto
{
    public const int NomeMax = 80;

    public int Id { get; init; }
    public string Nome { get; private set; } = "";
    /// <summary>#RRGGBB</summary>
    public string Cor { get; private set; } = "#607D8B";
    public bool Ativo { get; private set; } = true;

    public Projeto(int id, string nome, string cor, bool ativo)
    {
        Id = id;
        Renomear(nome);
        DefinirCor(cor);
        Ativo = ativo;
    }

    public void Renomear(string nome)
    {
        var n = (nome ?? "").Trim();
        if (n.Length is 0 or > NomeMax)
            throw new RegraDeNegocioException("projeto.nome", $"Nome do projeto deve ter de 1 a {NomeMax} caracteres.");
        Nome = n;
    }

    public void DefinirCor(string cor)
    {
        if (cor is null || !CorRegex().IsMatch(cor))
            throw new RegraDeNegocioException("projeto.cor", "Cor deve estar no formato #RRGGBB.");
        Cor = cor.ToUpperInvariant();
    }

    public void Arquivar() => Ativo = false;
    public void Desarquivar() => Ativo = true;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex CorRegex();
}
