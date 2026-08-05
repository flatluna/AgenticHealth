using System.Text.Json.Serialization;

namespace PersonalAgent.Skills;

/// <summary>
/// "Skill" pattern for ExerciseAgent - same approach as DietSkill/HumanOS's TutorSkill.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExerciseSkill
{
    Cardio,
    Strength,
    Flexibility,
    GeneralFitness,
}

public static class ExerciseSkillLibrary
{
    public static string InstructionsFor(ExerciseSkill skill) => skill switch
    {
        ExerciseSkill.Cardio =>
            "El usuario pregunta sobre entrenamiento cardiovascular (correr, ciclismo, resistencia). " +
            "Da recomendaciones progresivas según su nivel, evitando sobreentrenamiento.",

        ExerciseSkill.Strength =>
            "El usuario pregunta sobre entrenamiento de fuerza/pesas. Recomienda progresión gradual " +
            "de carga, técnica correcta y descanso adecuado entre sesiones del mismo grupo muscular.",

        ExerciseSkill.Flexibility =>
            "El usuario pregunta sobre flexibilidad, movilidad o estiramientos. Sugiere rutinas " +
            "seguras y recuerda calentar antes de estirar en frío.",

        ExerciseSkill.GeneralFitness =>
            "El usuario tiene una pregunta general de ejercicio/fitness sin un enfoque específico. " +
            "Da una respuesta balanceada considerando su nivel de experiencia si lo menciona.",

        _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, null),
    };
}

public static class ExerciseSkillSelector
{
    public static ExerciseSkill Select(string userMessage)
    {
        var text = userMessage.ToLowerInvariant();

        if (ContainsAny(text, "correr", "cardio", "resistencia", "ciclismo", "trotar"))
        {
            return ExerciseSkill.Cardio;
        }

        if (ContainsAny(text, "pesas", "fuerza", "musculación", "levantamiento", "hipertrofia"))
        {
            return ExerciseSkill.Strength;
        }

        if (ContainsAny(text, "estirar", "flexibilidad", "movilidad", "yoga"))
        {
            return ExerciseSkill.Flexibility;
        }

        return ExerciseSkill.GeneralFitness;
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(text.Contains);
}
