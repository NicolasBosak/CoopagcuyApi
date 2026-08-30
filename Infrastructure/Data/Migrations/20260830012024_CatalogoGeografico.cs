using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoopagcuyApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoGeografico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Provincias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provincias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cantones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProvinciaId = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cantones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cantones_Provincias_ProvinciaId",
                        column: x => x.ProvinciaId,
                        principalTable: "Provincias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Provincias",
                columns: new[] { "Id", "Activa", "Nombre" },
                values: new object[,]
                {
                    { 1, true, "Azuay" },
                    { 2, true, "Bolívar" },
                    { 3, true, "Cañar" },
                    { 4, true, "Carchi" },
                    { 5, true, "Chimborazo" },
                    { 6, true, "Cotopaxi" },
                    { 7, true, "El Oro" },
                    { 8, true, "Esmeraldas" },
                    { 9, true, "Galápagos" },
                    { 10, true, "Guayas" },
                    { 11, true, "Imbabura" },
                    { 12, true, "Loja" },
                    { 13, true, "Los Ríos" },
                    { 14, true, "Manabí" },
                    { 15, true, "Morona Santiago" },
                    { 16, true, "Napo" },
                    { 17, true, "Orellana" },
                    { 18, true, "Pastaza" },
                    { 19, true, "Pichincha" },
                    { 20, true, "Santa Elena" },
                    { 21, true, "Santo Domingo de los Tsáchilas" },
                    { 22, true, "Sucumbíos" },
                    { 23, true, "Tungurahua" },
                    { 24, true, "Zamora Chinchipe" }
                });

            migrationBuilder.InsertData(
                table: "Cantones",
                columns: new[] { "Id", "Activo", "Nombre", "ProvinciaId" },
                values: new object[,]
                {
                    { 1, true, "Cuenca", 1 },
                    { 2, true, "Girón", 1 },
                    { 3, true, "Gualaceo", 1 },
                    { 4, true, "Nabón", 1 },
                    { 5, true, "Paute", 1 },
                    { 6, true, "Pucará", 1 },
                    { 7, true, "San Fernando", 1 },
                    { 8, true, "Santa Isabel", 1 },
                    { 9, true, "Sígsig", 1 },
                    { 10, true, "Oña", 1 },
                    { 11, true, "Chordeleg", 1 },
                    { 12, true, "El Pan", 1 },
                    { 13, true, "Sevilla de Oro", 1 },
                    { 14, true, "Guachapala", 1 },
                    { 15, true, "Camilo Ponce Enríquez", 1 },
                    { 16, true, "Guaranda", 2 },
                    { 17, true, "Chillanes", 2 },
                    { 18, true, "Chimbo", 2 },
                    { 19, true, "Echeandía", 2 },
                    { 20, true, "San Miguel", 2 },
                    { 21, true, "Caluma", 2 },
                    { 22, true, "Las Naves", 2 },
                    { 23, true, "Azogues", 3 },
                    { 24, true, "Biblián", 3 },
                    { 25, true, "Cañar", 3 },
                    { 26, true, "La Troncal", 3 },
                    { 27, true, "El Tambo", 3 },
                    { 28, true, "Déleg", 3 },
                    { 29, true, "Suscal", 3 },
                    { 30, true, "Tulcán", 4 },
                    { 31, true, "Bolívar", 4 },
                    { 32, true, "Espejo", 4 },
                    { 33, true, "Mira", 4 },
                    { 34, true, "Montúfar", 4 },
                    { 35, true, "San Pedro de Huaca", 4 },
                    { 36, true, "Riobamba", 5 },
                    { 37, true, "Alausí", 5 },
                    { 38, true, "Colta", 5 },
                    { 39, true, "Chambo", 5 },
                    { 40, true, "Chunchi", 5 },
                    { 41, true, "Guamote", 5 },
                    { 42, true, "Guano", 5 },
                    { 43, true, "Pallatanga", 5 },
                    { 44, true, "Penipe", 5 },
                    { 45, true, "Cumandá", 5 },
                    { 46, true, "Latacunga", 6 },
                    { 47, true, "La Maná", 6 },
                    { 48, true, "Pangua", 6 },
                    { 49, true, "Pujilí", 6 },
                    { 50, true, "Salcedo", 6 },
                    { 51, true, "Saquisilí", 6 },
                    { 52, true, "Sigchos", 6 },
                    { 53, true, "Machala", 7 },
                    { 54, true, "Arenillas", 7 },
                    { 55, true, "Atahualpa", 7 },
                    { 56, true, "Balsas", 7 },
                    { 57, true, "Chilla", 7 },
                    { 58, true, "El Guabo", 7 },
                    { 59, true, "Huaquillas", 7 },
                    { 60, true, "Marcabelí", 7 },
                    { 61, true, "Pasaje", 7 },
                    { 62, true, "Piñas", 7 },
                    { 63, true, "Portovelo", 7 },
                    { 64, true, "Santa Rosa", 7 },
                    { 65, true, "Zaruma", 7 },
                    { 66, true, "Las Lajas", 7 },
                    { 67, true, "Esmeraldas", 8 },
                    { 68, true, "Eloy Alfaro", 8 },
                    { 69, true, "Muisne", 8 },
                    { 70, true, "Quinindé", 8 },
                    { 71, true, "San Lorenzo", 8 },
                    { 72, true, "Atacames", 8 },
                    { 73, true, "Rioverde", 8 },
                    { 74, true, "La Concordia", 8 },
                    { 75, true, "San Cristóbal", 9 },
                    { 76, true, "Isabela", 9 },
                    { 77, true, "Santa Cruz", 9 },
                    { 78, true, "Guayaquil", 10 },
                    { 79, true, "Alfredo Baquerizo Moreno", 10 },
                    { 80, true, "Balao", 10 },
                    { 81, true, "Balzar", 10 },
                    { 82, true, "Colimes", 10 },
                    { 83, true, "Daule", 10 },
                    { 84, true, "Durán", 10 },
                    { 85, true, "El Empalme", 10 },
                    { 86, true, "El Triunfo", 10 },
                    { 87, true, "Milagro", 10 },
                    { 88, true, "Naranjal", 10 },
                    { 89, true, "Naranjito", 10 },
                    { 90, true, "Palestina", 10 },
                    { 91, true, "Pedro Carbo", 10 },
                    { 92, true, "Samborondón", 10 },
                    { 93, true, "Santa Lucía", 10 },
                    { 94, true, "Salitre", 10 },
                    { 95, true, "San Jacinto de Yaguachi", 10 },
                    { 96, true, "Playas", 10 },
                    { 97, true, "Simón Bolívar", 10 },
                    { 98, true, "Coronel Marcelino Maridueña", 10 },
                    { 99, true, "Lomas de Sargentillo", 10 },
                    { 100, true, "Nobol", 10 },
                    { 101, true, "General Antonio Elizalde", 10 },
                    { 102, true, "Isidro Ayora", 10 },
                    { 103, true, "Ibarra", 11 },
                    { 104, true, "Antonio Ante", 11 },
                    { 105, true, "Cotacachi", 11 },
                    { 106, true, "Otavalo", 11 },
                    { 107, true, "Pimampiro", 11 },
                    { 108, true, "San Miguel de Urcuquí", 11 },
                    { 109, true, "Loja", 12 },
                    { 110, true, "Calvas", 12 },
                    { 111, true, "Catamayo", 12 },
                    { 112, true, "Celica", 12 },
                    { 113, true, "Chaguarpamba", 12 },
                    { 114, true, "Espíndola", 12 },
                    { 115, true, "Gonzanamá", 12 },
                    { 116, true, "Macará", 12 },
                    { 117, true, "Paltas", 12 },
                    { 118, true, "Puyango", 12 },
                    { 119, true, "Saraguro", 12 },
                    { 120, true, "Sozoranga", 12 },
                    { 121, true, "Zapotillo", 12 },
                    { 122, true, "Pindal", 12 },
                    { 123, true, "Quilanga", 12 },
                    { 124, true, "Olmedo", 12 },
                    { 125, true, "Babahoyo", 13 },
                    { 126, true, "Baba", 13 },
                    { 127, true, "Montalvo", 13 },
                    { 128, true, "Puebloviejo", 13 },
                    { 129, true, "Quevedo", 13 },
                    { 130, true, "Urdaneta", 13 },
                    { 131, true, "Ventanas", 13 },
                    { 132, true, "Vínces", 13 },
                    { 133, true, "Palenque", 13 },
                    { 134, true, "Buena Fe", 13 },
                    { 135, true, "Valencia", 13 },
                    { 136, true, "Mocache", 13 },
                    { 137, true, "Quinsaloma", 13 },
                    { 138, true, "Portoviejo", 14 },
                    { 139, true, "Bolívar", 14 },
                    { 140, true, "Chone", 14 },
                    { 141, true, "El Carmen", 14 },
                    { 142, true, "Flavio Alfaro", 14 },
                    { 143, true, "Jipijapa", 14 },
                    { 144, true, "Junín", 14 },
                    { 145, true, "Manta", 14 },
                    { 146, true, "Montecristi", 14 },
                    { 147, true, "Paján", 14 },
                    { 148, true, "Pichincha", 14 },
                    { 149, true, "Rocafuerte", 14 },
                    { 150, true, "Santa Ana", 14 },
                    { 151, true, "Sucre", 14 },
                    { 152, true, "Tosagua", 14 },
                    { 153, true, "24 de Mayo", 14 },
                    { 154, true, "Pedernales", 14 },
                    { 155, true, "Olmedo", 14 },
                    { 156, true, "Puerto López", 14 },
                    { 157, true, "Jama", 14 },
                    { 158, true, "Jaramijó", 14 },
                    { 159, true, "San Vicente", 14 },
                    { 160, true, "Morona", 15 },
                    { 161, true, "Gualaquiza", 15 },
                    { 162, true, "Limón Indanza", 15 },
                    { 163, true, "Palora", 15 },
                    { 164, true, "Santiago", 15 },
                    { 165, true, "Sucúa", 15 },
                    { 166, true, "Huamboya", 15 },
                    { 167, true, "San Juan Bosco", 15 },
                    { 168, true, "Taisha", 15 },
                    { 169, true, "Logroño", 15 },
                    { 170, true, "Pablo Sexto", 15 },
                    { 171, true, "Tiwintza", 15 },
                    { 172, true, "Tena", 16 },
                    { 173, true, "Archidona", 16 },
                    { 174, true, "El Chaco", 16 },
                    { 175, true, "Quijos", 16 },
                    { 176, true, "Carlos Julio Arosemena Tola", 16 },
                    { 177, true, "Orellana", 17 },
                    { 178, true, "Aguarico", 17 },
                    { 179, true, "La Joya de los Sachas", 17 },
                    { 180, true, "Loreto", 17 },
                    { 181, true, "Pastaza", 18 },
                    { 182, true, "Mera", 18 },
                    { 183, true, "Santa Clara", 18 },
                    { 184, true, "Arajuno", 18 },
                    { 185, true, "Quito", 19 },
                    { 186, true, "Cayambe", 19 },
                    { 187, true, "Mejía", 19 },
                    { 188, true, "Pedro Moncayo", 19 },
                    { 189, true, "Rumiñahui", 19 },
                    { 190, true, "San Miguel de los Bancos", 19 },
                    { 191, true, "Pedro Vicente Maldonado", 19 },
                    { 192, true, "Puerto Quito", 19 },
                    { 193, true, "Santa Elena", 20 },
                    { 194, true, "La Libertad", 20 },
                    { 195, true, "Salinas", 20 },
                    { 196, true, "Santo Domingo", 21 },
                    { 197, true, "Lago Agrio", 22 },
                    { 198, true, "Gonzalo Pizarro", 22 },
                    { 199, true, "Putumayo", 22 },
                    { 200, true, "Shushufindi", 22 },
                    { 201, true, "Sucumbíos", 22 },
                    { 202, true, "Cascales", 22 },
                    { 203, true, "Cuyabeno", 22 },
                    { 204, true, "Ambato", 23 },
                    { 205, true, "Baños de Agua Santa", 23 },
                    { 206, true, "Cevallos", 23 },
                    { 207, true, "Mocha", 23 },
                    { 208, true, "Patate", 23 },
                    { 209, true, "Quero", 23 },
                    { 210, true, "San Pedro de Pelileo", 23 },
                    { 211, true, "Santiago de Píllaro", 23 },
                    { 212, true, "Tisaleo", 23 },
                    { 213, true, "Zamora", 24 },
                    { 214, true, "Chinchipe", 24 },
                    { 215, true, "Nangaritza", 24 },
                    { 216, true, "Yacuambi", 24 },
                    { 217, true, "Yantzaza", 24 },
                    { 218, true, "El Pangui", 24 },
                    { 219, true, "Centinela del Cóndor", 24 },
                    { 220, true, "Palanda", 24 },
                    { 221, true, "Paquisha", 24 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cantones_ProvinciaId_Nombre",
                table: "Cantones",
                columns: new[] { "ProvinciaId", "Nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Provincias_Nombre",
                table: "Provincias",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cantones");

            migrationBuilder.DropTable(
                name: "Provincias");
        }
    }
}
