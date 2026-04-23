using APBD_TASK6.Enums;

namespace APBD_TASK6.DTOs;

public class UpdateAppointmentRequestDTO {
    public int IdPatient { get; set; }
    public int IdDoctor { get; set; }
    public DateTime AppointmentDate { get; set; }
    public AppointmentStatus Status { get; set; }
    public string Reason { get; set; } = null!;
    public string? InternalNotes { get; set; }
}