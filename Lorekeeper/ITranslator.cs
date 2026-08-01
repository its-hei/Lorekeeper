using System.Threading.Tasks;

namespace Lorekeeper;

public interface ITranslator
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string npcName,
        TranslationContext context);
}
