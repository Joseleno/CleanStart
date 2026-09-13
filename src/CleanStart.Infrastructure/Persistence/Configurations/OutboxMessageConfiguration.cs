using CleanStart.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanStart.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia a tabela de outbox.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox_messages");

        builder.HasKey(mensagem => mensagem.Id);

        builder.Property(mensagem => mensagem.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(mensagem => mensagem.Type)
            .HasColumnName("type")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(mensagem => mensagem.Content)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(mensagem => mensagem.OccurredOn)
            .HasColumnName("occurred_on")
            .IsRequired();

        builder.Property(mensagem => mensagem.ProcessedOn)
            .HasColumnName("processed_on");

        builder.Property(mensagem => mensagem.Error)
            .HasColumnName("error");

        // Índice parcial sobre o que ainda não foi despachado. É a única consulta que o despachante faz, e ela
        // roda em loop: sem o filtro, o índice cresceria com todo o histórico de mensagens já enviadas.
        builder.HasIndex(mensagem => mensagem.OccurredOn)
            .HasFilter("processed_on IS NULL")
            .HasDatabaseName("ix_outbox_messages_pendentes");
    }
}
