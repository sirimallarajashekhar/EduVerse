namespace EduVerse.Contracts;

public sealed record GradeBandCreate(string Grade,decimal MinPercent,decimal MaxPercent,decimal Points);
public sealed record GradingSchemeCreate(Guid CampusId,string Code,string Name,IReadOnlyList<GradeBandCreate> Bands);
public sealed record GradingSchemeRecord(Guid Id,Guid CampusId,string Code,string Name,IReadOnlyList<GradeBandCreate> Bands);
public sealed record ExamCreate(Guid CampusId,Guid AcademicYearId,Guid SchemeId,string Code,string Title,string Type);
public sealed record ExamRecord(Guid Id,Guid CampusId,Guid AcademicYearId,Guid SchemeId,string Code,string Title,string Type,string Status);
public sealed record AssessmentCreate(Guid CampusId,Guid ExamId,Guid AcademicItemId,Guid SubjectId,DateOnly ScheduledOn,
    TimeOnly? StartsAt,int? DurationMinutes,decimal MaxMarks,decimal PassMarks,decimal Credits);
public sealed record AssessmentRecord(Guid Id,Guid ExamId,Guid AcademicItemId,Guid SubjectId,DateOnly ScheduledOn,
    decimal MaxMarks,decimal PassMarks,decimal Credits);
public sealed record MarkCreate(Guid CampusId,Guid AssessmentId,Guid EnrollmentId,decimal? Marks,bool IsAbsent);
public sealed record MarkRecord(Guid Id,Guid AssessmentId,Guid EnrollmentId,decimal? Marks,bool IsAbsent);
public sealed record ResultLine(string Subject,string Grade,decimal? Marks,decimal MaxMarks,decimal Percentage,decimal Points,bool Passed,bool Absent);
public sealed record ResultRecord(Guid ExamId,Guid EnrollmentId,string Student,string AdmissionNumber,string Exam,
    decimal Total,decimal Maximum,decimal Percentage,decimal Gpa,bool Passed,IReadOnlyList<ResultLine> Subjects);
public sealed record ExamAnalytics(int Students,decimal AveragePercentage,decimal PassPercentage,decimal HighestPercentage,decimal LowestPercentage);
public sealed record CgpaRecord(Guid StudentId,decimal Cgpa,int Exams);
