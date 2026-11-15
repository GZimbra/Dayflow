using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class LembretesTests
{
    private static readonly TimeZoneInfo Sp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static DateTime Utc(int a, int m, int d, int h, int min = 0) => new(a, m, d, h, min, 0, DateTimeKind.Utc);
    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Sp);

    [Fact]
    public void Proximo_e_a_hora_cheia_seguinte_dentro_da_janela()
    {
        var cfg = new ConfiguracaoLembretes(true);
        // segunda 02/03/2026 10:20 local (-03) = 13:20Z
        Assert.Equal(new DateTime(2026, 3, 2, 11, 0, 0), Local(AgendaLembretes.Proximo(cfg, Utc(2026, 3, 2, 13, 20), Sp)!.Value));
        // exatamente 11:00 local: estritamente depois -> 12:00
        Assert.Equal(new DateTime(2026, 3, 2, 12, 0, 0), Local(AgendaLembretes.Proximo(cfg, Utc(2026, 3, 2, 14, 0), Sp)!.Value));
    }

    [Fact]
    public void Depois_do_fim_da_janela_pula_para_o_proximo_dia_util()
    {
        var cfg = new ConfiguracaoLembretes(true);
        // sexta 06/03 18:30 local -> segunda 09/03 08:00
        Assert.Equal(new DateTime(2026, 3, 9, 8, 0, 0), Local(AgendaLembretes.Proximo(cfg, Utc(2026, 3, 6, 21, 30), Sp)!.Value));
        // antes do início: 06:10 de terça -> 08:00 de terça
        Assert.Equal(new DateTime(2026, 3, 3, 8, 0, 0), Local(AgendaLembretes.Proximo(cfg, Utc(2026, 3, 3, 9, 10), Sp)!.Value));
    }

    [Fact]
    public void Silenciado_hoje_retoma_amanha_e_desativado_retorna_null()
    {
        var cfg = new ConfiguracaoLembretes(true, SilenciadoAte: new DateOnly(2026, 3, 2));
        Assert.Equal(new DateTime(2026, 3, 3, 8, 0, 0), Local(AgendaLembretes.Proximo(cfg, Utc(2026, 3, 2, 13, 0), Sp)!.Value));
        Assert.Null(AgendaLembretes.Proximo(cfg with { Ativo = false }, Utc(2026, 3, 2, 13, 0), Sp));
    }

    [Fact]
    public void Hora_inexistente_no_horario_de_verao_e_pulada()
    {
        // 2018-11-04 (domingo) SP: 00:00 não existe. Janela 0..2 todos os dias.
        var cfg = new ConfiguracaoLembretes(true, 0, 2, DiasSemana.Todos);
        var p = AgendaLembretes.Proximo(cfg, Utc(2018, 11, 4, 2, 30), Sp)!.Value;   // 23:30 local de 03/11
        Assert.Equal(Utc(2018, 11, 4, 3, 0), p);   // 01:00 local (-02)
    }

    [Fact]
    public void Janela_invalida_e_rejeitada()
    {
        Assert.Throws<RegraDeNegocioException>(() =>
            AgendaLembretes.Proximo(new ConfiguracaoLembretes(true, 18, 8), Utc(2026, 3, 2, 12), Sp));
    }

    [Fact]
    public async Task Conteudo_prioriza_cronometro_e_omite_quando_nada_pendente()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        Assert.Null(await a.Lembretes.MontarAsync());

        var t = await a.NovaTarefa("Inventário cíclico", D(2026, 3, 2));
        await a.Rotinas.CriarAsync(new DadosRotina("Checklist", null, null, DiasSemana.Uteis, D(2026, 3, 2)));
        var c = await a.Lembretes.MontarAsync();
        Assert.Equal("1 pendente, 1 rotina por fazer", c!.Titulo);
        Assert.Equal(t, c.ProximaTarefaId);

        Assert.True(await a.Lembretes.IniciarProximaAsync());
        a.IrPara(2026, 3, 2, 10, 40);
        var c2 = await a.Lembretes.MontarAsync();
        Assert.True(c2!.CronometroRodando);
        Assert.Equal("Em andamento · 1h40", c2.Titulo);
        Assert.StartsWith("Inventário cíclico", c2.Texto);
    }
}
