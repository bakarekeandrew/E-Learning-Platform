using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface IAssignmentService
    {
        Task<int> CreateAssignmentAsync(int courseId, string title, string instructions, DateTime dueDate, int maxScore, int createdBy);
        Task<bool> UpdateAssignmentAsync(int assignmentId, string title, string instructions, DateTime dueDate, int maxScore, int updatedBy);
        Task<bool> DeleteAssignmentAsync(int assignmentId, int deletedBy);
        Task<bool> SubmitAssignmentAsync(int assignmentId, int userId, string submissionText, string fileUrl);
        Task<bool> GradeAssignmentAsync(int assignmentId, int studentId, decimal grade, string feedback, int gradedBy);
        Task<bool> AssignToStudentAsync(int assignmentId, int studentId, int assignedBy);
        Task<bool> RemoveFromStudentAsync(int assignmentId, int studentId, int removedBy);
        Task<IEnumerable<dynamic>> GetStudentAssignmentsAsync(int studentId, string filter = "all");
        Task<IEnumerable<dynamic>> GetInstructorAssignmentsAsync(int instructorId, int? courseId = null);
    }
} 