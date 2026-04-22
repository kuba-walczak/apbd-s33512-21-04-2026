using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase {
        private readonly string _connectionString;
        
        public AppointmentsController(IConfiguration configuration) {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing 'DefaultConnection' in appsettings.json.");
        }

        [HttpGet]
        public async Task<IActionResult> GetAppointments([FromQuery]string? status, [FromQuery]string? patientLastName) {
            const string sql = """
                                SELECT
                                    a.IdAppointment,
                                    a.AppointmentDate,
                                    a.Status,
                                    a.Reason,
                                    p.FirstName + N' ' + p.LastName AS PatientFullName,
                                    p.Email AS PatientEmail
                                FROM dbo.Appointments a
                                JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                                WHERE (@Status IS NULL OR a.Status = @Status)
                                    AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                                ORDER BY a.AppointmentDate;
                        """;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            
            command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);
            
            await connection.OpenAsync();
            
            await using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<AppointmentListDto>();

            while (await reader.ReadAsync()) {
                results.Add(new AppointmentListDto {
                    IdAppointment = reader.GetInt32(0),
                    AppointmentDate = reader.GetDateTime(1),
                    Status = reader.GetString(2),
                    Reason = reader.GetString(3),
                    PatientFullName = reader.GetString(4),
                    PatientEmail = reader.GetString(5),
                });
            }
            return Ok(results);
        }

        [HttpGet("{idAppointment:int}")]
        public async Task<IActionResult> GetAppointment([FromRoute] int idAppointment) {
            const string sql = """
                                   SELECT IdAppointment, IdPatient, IdDoctor, AppointmentDate,
                                          Status, Reason, InternalNotes, CreatedAt
                                   FROM dbo.Appointments
                                   WHERE IdAppointment = @IdAppointment;
                               """;

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);

            command.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound();

            var appointment = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(0),
                IdPatient = reader.GetInt32(1),
                IdDoctor = reader.GetInt32(2),
                AppointmentDate = reader.GetDateTime(3),
                Status = reader.GetString(4),
                Reason = reader.GetString(5),
                InternalNotes = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            };

            return Ok(appointment);
        }
        
        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody]CreateAppointmentRequestDto request) {
            if (request.AppointmentDate < DateTime.UtcNow) {
                return BadRequest(new ErrorResponseDto());
            }
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            int newId;

            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync()) {
                const string sql = """
                                            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Reason)
                                            OUTPUT INSERTED.IdAppointment
                                            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Reason, 'Scheduled');
                                  """;
                
                await using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                command.Parameters.AddWithValue("@Reason", request.Reason);

                    newId = (int)(await command.ExecuteScalarAsync())!;
                    await transaction.CommitAsync();
            }
            return CreatedAtRoute(nameof(GetAppointments), new { idAppointment = newId }, null);
        }

        [HttpPut("{idAppointment:int}")]
        public async Task<IActionResult> UpdateAppointment([FromRoute] int idAppointment, [FromBody] UpdateAppointmentRequestDTO request)
        {
            var sql = """
                        UPDATE dbo.Appointments
                        SET
                          IdDoctor = @IdDoctor,
                          AppointmentDate = @AppointmentDate,
                          Status = @Status,
                          Reason = @Reason,
                          InternalNotes = @InternalNotes
                        OUTPUT INSERTED.*
                        WHERE IdAppointment = @IdAppointment;
                      """;
            
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            
            command.Parameters.AddWithValue("@IdAppointment", idAppointment);
            command.Parameters.AddWithValue("@IdDoctor", (object?)request.IdDoctor ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppointmentDate", (object?)request.AppointmentDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", (object?)request.Status ?? DBNull.Value);
            command.Parameters.AddWithValue("@Reason", (object?)request.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);

            await connection.OpenAsync();
            
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound();

            var appointment = new AppointmentDetailsDto()
            {
                IdAppointment = reader.GetInt32(0),
                IdPatient = reader.GetInt32(1),
                IdDoctor = reader.GetInt32(2),
                AppointmentDate = reader.GetDateTime(3),
                Status = reader.GetString(4),
                Reason = reader.GetString(5),
                InternalNotes = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            };

            return Ok(appointment);
        }
        
        [HttpDelete("{idAppointment:int}")]
        public async Task<IActionResult> DeleteAppointment([FromRoute] int idAppointment) {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                                        DELETE FROM dbo.Appointments
                                        WHERE idAppointment = @IdAppointment;
                                     """;
            
            await using var deleteCommand = new SqlCommand(sql, connection);
            deleteCommand.Parameters.AddWithValue("@idAppointment", idAppointment);
            await deleteCommand.ExecuteNonQueryAsync();
            
            return NoContent();
        }
    }
}