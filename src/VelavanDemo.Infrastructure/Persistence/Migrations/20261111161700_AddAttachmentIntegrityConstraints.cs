using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VelavanDemo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductImages_SortOrder",
                table: "ProductImages",
                sql: "[SortOrder] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attachments_Dimensions",
                table: "Attachments",
                sql: "([Width] IS NULL AND [Height] IS NULL) OR ([Width] > 0 AND [Height] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attachments_SizeBytes",
                table: "Attachments",
                sql: "[SizeBytes] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductImages_SortOrder",
                table: "ProductImages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Attachments_Dimensions",
                table: "Attachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Attachments_SizeBytes",
                table: "Attachments");
        }
    }
}
