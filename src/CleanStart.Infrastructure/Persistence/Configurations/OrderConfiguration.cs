using CleanStart.Domain.Customers;
using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanStart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia o agregado <see cref="Order"/>.
/// </summary>
/// <remarks>
/// Tudo por Fluent API, nenhuma anotação no domínio: atributo de EF na entidade a acoplaria ao ORM, e a regra
/// de arquitetura que proíbe isso é verificada por teste.
/// </remarks>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("orders");

        // Identidade tipada por conversor de valor: a coluna é uuid, o domínio continua falando OrderId.
        // Sem isso, o EF trataria OrderId como tipo complexo e tentaria mapeá-lo como tabela.
        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasConversion(id => id.Value, valor => new OrderId(valor))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(order => order.CustomerId)
            .HasConversion(id => id.Value, valor => new CustomerId(valor))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(order => order.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(order => order.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(order => order.UpdatedAt)
            .HasColumnName("updated_at");

        // Total é calculado a partir dos itens — não existe coluna para ele. Sem este Ignore, o EF tentaria
        // persistir a propriedade e o modelo nem construiria.
        builder.Ignore(order => order.Total);

        // DomainEvents é estado em memória até o despacho; nunca vai para o banco.
        builder.Ignore(order => order.DomainEvents);

        // Acesso à coleção pelo campo privado: a propriedade é IReadOnlyCollection, e o EF precisa inserir
        // itens ao materializar. É o que permite manter a coleção fechada para o resto do código.
        builder.Metadata
            .FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey("order_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(order => order.CustomerId)
            .HasDatabaseName("ix_orders_customer_id");
    }
}
