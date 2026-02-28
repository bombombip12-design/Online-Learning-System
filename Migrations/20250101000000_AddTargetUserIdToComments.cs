using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinalASB.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetUserIdToComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetUserId",
                table: "Comments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TargetUserId",
                table: "Comments",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Users_TargetUserId",
                table: "Comments",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Users_TargetUserId",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TargetUserId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "Comments");
        }
    }
}

