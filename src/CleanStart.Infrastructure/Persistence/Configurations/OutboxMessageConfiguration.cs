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

        builder.Property(mensagem => mensagem.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(mensagem => mensagem.NextAttemptOn)
            .HasColumnName("next_attempt_on")
            .IsRequired();

        // Índice parcial sobre o que ainda não foi despachado. É a única consulta que o despachante faz, e ela
        // roda em loop: sem o filtro, o índice cresceria com todo o histórico de mensagens já enviadas.
        //
        // A ordem das colunas segue a regra "quem elimina linhas vem antes de quem ordena": next_attempt_on é o
        // predicado de corte (<= agora), então o índice vira um range scan que para no ponto certo; com
        // occurred_on na frente, um incidente com milhares de mensagens em backoff faria o planejador varrer a
        // fila inteira descartando linha a linha. occurred_on fica como desempate, preservando o FIFO entre
        // mensagens igualmente elegíveis.
        //
        // Attempts fica DE FORA de propósito: baixa cardinalidade não ajuda o índice, e no filtro parcial
        // hardcodaria a política de tentativas no schema — mudar o máximo viraria migration.
        builder.HasIndex(mensagem => new { mensagem.NextAttemptOn, mensagem.OccurredOn })
            .HasFilter("processed_on IS NULL")
            .HasDatabaseName("ix_outbox_messages_pendentes");
    }
}
