using CleanStart.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanStart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia o agregado <see cref="Customer"/>.
/// </summary>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customers");

        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Id)
            .HasConversion(id => id.Value, valor => new CustomerId(valor))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(customer => customer.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        // Email e Document são value objects de um só componente, então viram coluna simples por conversor —
        // owned type aqui só acrescentaria cerimônia.
        builder.Property(customer => customer.Email)
            .HasConversion(
                email => email.Value,
                valor => CleanStart.Domain.ValueObjects.Email.Of(valor).Value)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(customer => customer.Document)
            .HasConversion(
                document => document.Value,
                valor => CleanStart.Domain.ValueObjects.Document.Of(valor).Value)
            .HasColumnName("document")
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(customer => customer.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(customer => customer.UpdatedAt)
            .HasColumnName("updated_at");



        // Autoria: sem este mapeamento o EF cai na convenção padrão e gera "CreatedBy"/"UpdatedBy" em PascalCase,


        // fora do snake_case de todas as outras colunas. Foi o que aconteceu na primeira migration.


        builder.Property(customer => customer.CreatedBy)
            .HasColumnName("created_by");



        builder.Property(customer => customer.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(customer => customer.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(customer => customer.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Ignore(customer => customer.DomainEvents);

        // Índice único no documento: a unicidade é garantida pelo banco, não só pela consulta prévia do caso
        // de uso. Entre o SELECT e o INSERT cabem duas requisições simultâneas — só a constraint fecha a porta.
        //
        // O filtro parcial é necessário por causa do soft delete: sem ele, um cliente excluído continuaria
        // bloqueando o recadastro do mesmo documento.
        builder.HasIndex(customer => customer.Document)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_customers_document");
    }
}
