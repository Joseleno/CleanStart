namespace CleanStart.Domain;

/// <summary>
/// Âncora para localizar este assembly por reflexão.
/// </summary>
/// <remarks>
/// Os testes de arquitetura precisam de um <c>Assembly</c> para inspecionar, e usar um tipo de
/// domínio para isso cria um acoplamento invisível: o dia em que a entidade escolhida for renomeada
/// ou movida, o teste quebra por um motivo que nada tem a ver com arquitetura. Um marcador sem
/// comportamento torna essa dependência explícita e estável.
/// </remarks>
public sealed class AssemblyMarker
{
    private AssemblyMarker()
    {
    }
}
