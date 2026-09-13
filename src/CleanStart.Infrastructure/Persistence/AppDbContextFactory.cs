using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanStart.Infrastructure.Persistence;

/// <summary>
/// Cria o contexto para as ferramentas de linha de comando do EF Core.
/// </summary>
/// <remarks>
/// <para>
/// O <c>dotnet ef</c> precisa instanciar o <see cref="AppDbContext"/> para gerar migration, e por padrão ele
/// procura isso no projeto de startup — que aqui ainda não existe. Esta fábrica torna a Infrastructure
/// autossuficiente para gerar migration, sem depender da Api.
/// </para>
/// <para>
/// <b>A connection string daqui não é usada para nada além de gerar SQL.</b> A geração de migration é offline: o
/// EF precisa apenas saber que o provider é PostgreSQL, para escrever o DDL no dialeto certo. Nenhuma conexão é
/// aberta, e nenhum banco precisa existir.
/// </para>
/// <para>
/// Por isso ela é um literal inócuo, sem segredo. A connection string de verdade vem da configuração validada
/// (<c>DatabaseOptions</c>), que é o caminho que a aplicação usa em execução.
/// </para>
/// </remarks>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=cleanstart_design_time;Username=postgres;Password=design_time")
            .Options;

        return new AppDbContext(options);
    }
}
