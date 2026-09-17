namespace CleanStart.Api;

/// <summary>
/// Âncora para localizar este assembly por reflexão.
/// </summary>
/// <remarks>
/// Os testes de arquitetura precisam de um <c>Assembly</c> para inspecionar, e usar um tipo de
/// domínio para isso cria um acoplamento invisível: o dia em que a entidade escolhida for renomeada
/// ou movida, o teste quebra por um motivo que nada tem a ver com arquitetura. Um marcador sem
/// comportamento torna essa dependência explícita e estável.
/// <para>
/// A Api ganhou o seu na T9.2, quando as regras passaram a inspecionar esta camada — as outras três o têm
/// desde a T0.3. Sem ele, o caminho óbvio seria ancorar num módulo Carter, que é exatamente o tipo de
/// acoplamento que este marcador existe para evitar.
/// </para>
/// </remarks>
public sealed class AssemblyMarker
{
    private AssemblyMarker()
    {
    }
}
