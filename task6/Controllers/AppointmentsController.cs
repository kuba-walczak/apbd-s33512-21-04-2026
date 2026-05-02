using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/appointments")]
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
                               SELECT
                                    a.IdAppointment,
                                    a.IdPatient,
                                    a.IdDoctor,
                                    a.AppointmentDate,
                                    a.Status,
                                    a.Reason,
                                    a.InternalNotes,
                                    a.CreatedAt,
                                    p.Email,
                                    p.PhoneNumber,
                                    d.LicenseNumber
                                FROM dbo.Appointments a
                                JOIN dbo.Patients p
                                    ON a.IdPatient = p.IdPatient
                                JOIN dbo.Doctors d
                                    ON a.IdDoctor = d.IdDoctor
                                WHERE a.IdAppointment = @IdAppointment;
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
                CreatedAt = reader.GetDateTime(7),
                PatientEmail = reader.IsDBNull(8) ? null : reader.GetString(8),
                PatientPhone = reader.IsDBNull(9) ? null : reader.GetString(9),
                DoctorLicenseNumber = reader.IsDBNull(10) ? null : reader.GetString(10)
            };

            return Ok(appointment);
        }
        
        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody]CreateAppointmentRequestDto request) {
            if (request.AppointmentDate < DateTime.UtcNow) {
                return BadRequest(new ErrorResponseDto("Appointment date cannot be in the past."));
            }
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

                const string checkPatientSql = """
                                                    SELECT 1
                                                    FROM dbo.Patients
                                                    WHERE IdPatient = @IdPatient
                                                      AND IsActive = 1;
                                               """;
                await using var checkPatientCommand = new SqlCommand(checkPatientSql, connection);
                checkPatientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                var patientIsActive = await checkPatientCommand.ExecuteScalarAsync();
                if (patientIsActive == null) {
                    return BadRequest(new ErrorResponseDto("Patient does not exist or is inactive."));
                }

                const string checkDoctorSql = """
                                                   SELECT 1
                                                   FROM dbo.Doctors
                                                   WHERE IdDoctor = @IdDoctor
                                                     AND IsActive = 1;
                                              """;
                await using var checkDoctorCommand = new SqlCommand(checkDoctorSql, connection);
                checkDoctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                var doctorIsActive = await checkDoctorCommand.ExecuteScalarAsync();
                if (doctorIsActive == null) {
                    return BadRequest(new ErrorResponseDto("Doctor does not exist or is inactive."));
                }
            
                const string checkAppointmentSql = """
                                            SELECT 1
                                            FROM dbo.Appointments
                                            WHERE IdDoctor = @IdDoctor
                                            AND AppointmentDate = @AppointmentDate;
                                        """;
                await using var checkCommand = new SqlCommand(checkAppointmentSql, connection);
                checkCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                checkCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            
                var exists = await checkCommand.ExecuteScalarAsync();

                if (exists != null) {
                    return Conflict(new ErrorResponseDto("Doctor already has an appointment at this time."));
                }

                const string sql = """
                                     INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
                                     OUTPUT INSERTED.IdAppointment
                                     VALUES (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled', @Reason);
                                   """;
                
                await using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
                command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                command.Parameters.AddWithValue("@Reason", request.Reason);

                var newId = (int)(await command.ExecuteScalarAsync())!;
            return CreatedAtAction(nameof(GetAppointment), new { idAppointment = newId }, null);
        }

        [HttpPut("{idAppointment:int}")]
        public async Task<IActionResult> UpdateAppointment([FromRoute] int idAppointment, [FromBody] UpdateAppointmentRequestDTO request) {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string checkAppointmentSql = """
                                               SELECT Status, AppointmentDate
                                               FROM dbo.Appointments
                                               WHERE IdAppointment = @IdAppointment;
                                           """;

            await using var checkAppointmentCommand = new SqlCommand(checkAppointmentSql, connection);
            checkAppointmentCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await using var currentAppointmentReader = await checkAppointmentCommand.ExecuteReaderAsync();
            if (!await currentAppointmentReader.ReadAsync()) {
                return NotFound();
            }

            var currentStatus = currentAppointmentReader.GetString(0);
            var currentAppointmentDate = currentAppointmentReader.GetDateTime(1);
            await currentAppointmentReader.CloseAsync();

            if (currentStatus == "Completed" && currentAppointmentDate != request.AppointmentDate) {
                return Conflict(new ErrorResponseDto("Cannot change date of a completed appointment."));
            }
            
            var checkPatientSql = """
                                    SELECT 1
                                    FROM dbo.Patients
                                    WHERE IdPatient = @IdPatient
                                     AND IsActive = 1;
                                  """;
            await using var checkPatientCommand = new SqlCommand(checkPatientSql, connection);
            checkPatientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            
            var patientReader = await checkPatientCommand.ExecuteScalarAsync();
            if (patientReader == null) {
                return BadRequest(new ErrorResponseDto("Patient does not exist or is inactive."));
            }
            
            const string checkDoctorSql = """
                                               SELECT 1
                                               FROM dbo.Doctors
                                               WHERE IdDoctor = @IdDoctor
                                                 AND IsActive = 1;
                                          """;
            await using var checkDoctorCommand = new SqlCommand(checkDoctorSql, connection);
            checkDoctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            var doctorIsActive = await checkDoctorCommand.ExecuteScalarAsync();
            if (doctorIsActive == null) {
                return BadRequest(new ErrorResponseDto("Doctor does not exist or is inactive."));
            }

            if (request.AppointmentDate != currentAppointmentDate) {
                const string checkConflictSql = """
                                                SELECT 1
                                                FROM dbo.Appointments
                                                WHERE IdDoctor = @IdDoctor
                                                  AND AppointmentDate = @AppointmentDate
                                                  AND IdAppointment != @IdAppointment;
                                                """;
                await using var checkConflictCommand = new SqlCommand(checkConflictSql, connection);
                checkConflictCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
                checkConflictCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
                checkConflictCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

                var hasConflict = await checkConflictCommand.ExecuteScalarAsync();
                if (hasConflict != null) {
                    return Conflict(new ErrorResponseDto("Doctor already has an appointment at this time."));
                }
            }
            
            var sql = """
                        UPDATE dbo.Appointments
                        SET
                          IdPatient = @IdPatient,
                          IdDoctor = @IdDoctor,
                          AppointmentDate = @AppointmentDate,
                          Status = @Status,
                          Reason = @Reason,
                          InternalNotes = @InternalNotes
                        OUTPUT INSERTED.*
                        WHERE IdAppointment = @IdAppointment;
                      """;
            
            await using var command = new SqlCommand(sql, connection);
            
            command.Parameters.AddWithValue("@IdAppointment", idAppointment);
            command.Parameters.AddWithValue("@IdPatient", (object?)request.IdPatient ?? DBNull.Value);
            command.Parameters.AddWithValue("@IdDoctor", (object?)request.IdDoctor ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppointmentDate", (object?)request.AppointmentDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", request.Status);
            command.Parameters.AddWithValue("@Reason", (object?)request.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
            
            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound();

            var appointment = new AppointmentDetailsDto {
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

            const string checkStatusSql = """
                                          SELECT Status
                                          FROM dbo.Appointments
                                          WHERE IdAppointment = @IdAppointment;
                                          """;

            await using var checkStatusCommand = new SqlCommand(checkStatusSql, connection);
            checkStatusCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await using var statusReader = await checkStatusCommand.ExecuteReaderAsync();
            if (!await statusReader.ReadAsync()) {
                return NotFound(new ErrorResponseDto("Appointment not found."));
            }

            var status = statusReader.GetString(0);
            if (status == "Completed") {
                return Conflict(new ErrorResponseDto("Completed appointments cannot be deleted."));
            }
            await statusReader.CloseAsync();

            const string sql = """
                                        DELETE FROM dbo.Appointments
                                        WHERE idAppointment = @IdAppointment;
                                     """;
            
            await using var deleteCommand = new SqlCommand(sql, connection);
            deleteCommand.Parameters.AddWithValue("@idAppointment", idAppointment);
            var result = await deleteCommand.ExecuteNonQueryAsync();
            if (result == 0) {
                return NotFound(new ErrorResponseDto("Appointment not found."));
            }
            
            
            
            return NoContent();
        }
    }
}