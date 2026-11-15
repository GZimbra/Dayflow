namespace Dayflow.Core.Domain;

/// <summary>Violação de regra de domínio. Mensagem é segura para exibir ao usuário (sem dados pessoais).</summary>
public sealed class RegraDeNegocioException : Exception
{
    public string Codigo { get; }

    public RegraDeNegocioException(string codigo, string mensagem) : base(mensagem) => Codigo = codigo;
}
