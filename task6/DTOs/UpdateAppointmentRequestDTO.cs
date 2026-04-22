namespace APBD_TASK6.DTOs;

public class UpdateAppointmentRequestDTO {
    public int IdDoctor { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string? InternalNotes { get; set; }
}