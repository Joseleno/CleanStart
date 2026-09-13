using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanStart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TentativasDeEntregaNoOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pendentes",
                table: "outbox_messages");

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // O default gerado pelo EF seria DateTimeOffset.MinValue (0001-01-01), porque o modelo declara a
            // coluna não-nula sem valor padrão. Trocado por now() de propósito: numa tabela que já tenha
            // mensagens pendentes, a data mínima as marcaria todas como elegíveis desde sempre — e é um valor
            // que não significa nada para quem for ler a tabela depois. O default serve só às linhas
            // preexistentes; daqui em diante quem preenche o campo é o DomainEventInterceptor, com o OccurredOn
            // do evento.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_on",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pendentes",
                table: "outbox_messages",
                columns: new[] { "next_attempt_on", "occurred_on" },
                filter: "processed_on IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pendentes",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_on",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pendentes",
                table: "outbox_messages",
                column: "occurred_on",
                filter: "processed_on IS NULL");
        }
    }
}
