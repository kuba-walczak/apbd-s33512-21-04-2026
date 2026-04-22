namespace APBD_TASK6.DTOs;

public class AppointmentDetailsDto {
    public int IdAppointment { get; set; }
    public int IdPatient { get; set; }
    public int IdDoctor { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string? InternalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}