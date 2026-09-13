using CleanStart.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanStart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="OrderItem"/>, que só existe dentro de um pedido.
/// </summary>
internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(item => item.ProductId)
            .HasConversion(id => id.Value, valor => new ProductId(valor))
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        // Money como owned type: vira duas colunas nesta mesma tabela, em vez de uma tabela própria.
        // É o mapeamento correto para value object — ele não tem identidade e não existe sem o dono.
        builder.OwnsOne(item => item.UnitPrice, preco =>
        {
            preco.Property(money => money.Amount)
                .HasColumnName("unit_price_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            preco.Property(money => money.Currency)
                .HasColumnName("unit_price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // Total do item é UnitPrice × Quantity, calculado em memória.
        builder.Ignore(item => item.Total);
    }
}
