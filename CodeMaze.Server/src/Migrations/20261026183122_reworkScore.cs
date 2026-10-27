using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeMaze.Server.Migrations
{
    /// <inheritdoc />
    public partial class reworkScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId_Value",
                table: "Scores");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "Scores",
                newName: "MazeSize");

            migrationBuilder.RenameColumn(
                name: "Program",
                table: "Scores",
                newName: "DSLProgram");

            migrationBuilder.AddColumn<int>(
                name: "Difficulty",
                table: "Scores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Efficiency",
                table: "Scores",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "Perfect",
                table: "Scores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "Points",
                table: "Scores",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId",
                table: "Scores",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_PlayerId",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "Efficiency",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "Perfect",
                table: "Scores");

            migrationBuilder.DropColumn(
                name: "Points",
                table: "Scores");

            migrationBuilder.RenameColumn(
                name: "MazeSize",
                table: "Scores",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "DSLProgram",
                table: "Scores",
                newName: "Program");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PlayerId_Value",
                table: "Scores",
                columns: new[] { "PlayerId", "Value" });
        }
    }
}
