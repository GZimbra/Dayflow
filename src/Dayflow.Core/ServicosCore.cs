using Dayflow.Core.Data;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core;

public static class ServicosCore
{
    public static IServiceCollection AddDayflowCore(this IServiceCollection s, string caminhoBanco)
    {
        s.TryAddSingleton(TimeProvider.System);
        s.TryAddSingleton<IRelogioCivil, RelogioCivil>();
        s.AddSingleton(sp => new BancoDayflow(caminhoBanco, sp.GetRequiredService<ILoggerFactory>()));
        s.AddSingleton<ServicoProjetos>();
        s.AddSingleton<ServicoTarefas>();
        s.AddSingleton<ServicoCronometro>();
        s.AddSingleton<ServicoRegistros>();
        s.AddSingleton<ServicoRelatorios>();
        s.AddSingleton<ServicoRotinas>();
        s.AddSingleton<ServicoLembretes>();
        s.AddSingleton<ServicoConclusoes>();
        s.AddSingleton<ServicoHorarios>();
        return s;
    }
}
