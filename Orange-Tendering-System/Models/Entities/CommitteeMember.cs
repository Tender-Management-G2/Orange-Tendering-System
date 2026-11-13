using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // Assignment of a user to a tender's Evaluation or Top Committee (4 each, checked in business logic).
    // The unique index also stops the same person from sitting on both committees of one tender.
    [Index(nameof(TenderId), nameof(UserId), IsUnique = true)]
    public class CommitteeMember
    {
        public int Id { get; set; }

        public int TenderId { get; set; }
        public Tender Tender { get; set; } = null!;

        public CommitteeType CommitteeType { get; set; }

        // Evaluation Committee seat of this member for this tender (Sourcing Manager, Department, Financial or
        // Technical representative). Null for Top Committee members. A filtered unique index in
        // ApplicationDbContext allows each responsibility only once per tender and committee.
        public CommitteeResponsibility? Responsibility { get; set; }

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public DateTime AssignedAt { get; set; }
    }
}
