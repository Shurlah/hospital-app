using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmunizationSystem.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentTimeAndTwilioSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "AppointmentTime",
                table: "Appointments",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.CreateIndex(
                name: "IX_SmsNotifications_ProviderMessageId",
                table: "SmsNotifications",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentDate_AppointmentTime",
                table: "Appointments",
                columns: new[] { "AppointmentDate", "AppointmentTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SmsNotifications_ProviderMessageId",
                table: "SmsNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_AppointmentDate_AppointmentTime",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "AppointmentTime",
                table: "Appointments");
        }
    }
}
