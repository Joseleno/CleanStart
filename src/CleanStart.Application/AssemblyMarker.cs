namespace CleanStart.Application;

/// <summary>
/// Âncora para localizar este assembly por reflexão.
/// </summary>
/// <remarks>
/// Ver <c>CleanStart.Domain.AssemblyMarker</c> para o motivo de não ancorar os testes de arquitetura
/// num tipo de verdade. Aqui ele servirá também para registrar o Mediator e os validators por varredura
/// de assembly, a partir da Fase 2.
/// </remarks>
public sealed class AssemblyMarker
{
    private AssemblyMarker()
    {
    }
}
