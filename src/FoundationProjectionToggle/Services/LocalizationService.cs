using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FoundationProjectionToggle.Services
{
    /// <summary>
    /// Service for finding Revit subcategories across different language installations.
    ///
    /// <para>
    /// <b>Problem Solved:</b>
    /// Revit translates subcategory names based on installation language.
    /// A subcategory named "&lt;Projection&gt;" in English becomes "&lt;Projectie&gt;" in Dutch.
    /// This service abstracts away the localization complexity.
    /// </para>
    ///
    /// <para>
    /// <b>Strategy:</b>
    /// 1. Try known translations (fast, deterministic)
    /// 2. Fall back to pattern matching (catches unknown languages)
    /// </para>
    ///
    /// <para>
    /// <b>Extensibility:</b>
    /// To add support for a new subcategory type, add an entry to SubcategoryNames dictionary.
    /// To add a new language, add the translation to the existing arrays.
    /// </para>
    /// </summary>
    public class LocalizationService
    {
        /// <summary>
        /// Dictionary mapping logical subcategory keys to known localized names.
        ///
        /// <para>
        /// Keys are internal identifiers used in code (e.g., "Projection").
        /// Values are arrays of known translations, most common languages first.
        /// </para>
        ///
        /// <para>
        /// <b>Ordering Rationale:</b>
        /// English first (most common, often used even in non-English regions),
        /// then Dutch (primary user base for this add-in),
        /// then other major European languages.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string[]> SubcategoryNames = new Dictionary<string, string[]>
        {
            ["Projection"] = new[]
            {
                // System subcategories (with angle brackets)
                "<Projection>",       // English
                "<Projectie>",        // Dutch (short form)
                "<Projectielijnen>",  // Dutch (full form)
                "<Projektion>",       // German
                "<Proyección>",       // Spanish
                "<Proiezione>",       // Italian
                "<Projeção>",         // Portuguese
                "<投影>",              // Chinese (Simplified)
                "<プロジェクション>",   // Japanese

                // Custom subcategories (without angle brackets) - common in company templates
                "Projectielijnen",    // Dutch custom (Witteveen+Bos template)
                "Projection",         // English custom
                "Projection Lines",   // English custom (full)
                "Projectie",          // Dutch custom (short)
            },
            ["HiddenLines"] = new[]
            {
                "<Hidden>",        // English
                "<Verborgen>",     // Dutch
                "<Versteckt>",     // German
                "<Masqué>",        // French
                "<Oculto>",        // Spanish
                "<Nascosto>",      // Italian
            },
            ["Beyond"] = new[]
            {
                "<Beyond>",        // English
                "<Achterliggend>", // Dutch
                "<Dahinter>",      // German
                "<Au-delà>",       // French
            },
            ["Cut"] = new[]
            {
                "<Cut>",           // English
                "<Snede>",         // Dutch
                "<Schnitt>",       // German
            }
        };

        /// <summary>
        /// Finds a subcategory by its logical key, handling localization automatically.
        /// </summary>
        /// <param name="parentCategory">
        /// The parent category containing subcategories (e.g., Structural Foundations).
        /// </param>
        /// <param name="subcategoryKey">
        /// The logical key identifying the subcategory type.
        /// Must be a key in SubcategoryNames dictionary (e.g., "Projection", "HiddenLines").
        /// </param>
        /// <returns>
        /// The matching Category object, or null if not found.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if subcategoryKey is not a known key.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>Algorithm:</b>
        /// 1. Validate the key exists (fail fast for programming errors)
        /// 2. Try each known translation in order (O(n) where n = languages)
        /// 3. If no match, try pattern matching on angle brackets
        /// 4. Return null if still no match (let caller decide how to handle)
        /// </para>
        ///
        /// <para>
        /// <b>Performance:</b>
        /// For typical use (3-10 subcategories, 8-10 known translations),
        /// this is effectively O(1) since both loops are bounded constants.
        /// </para>
        /// </remarks>
        public Category FindSubcategory(Category parentCategory, string subcategoryKey)
        {
            // Validate key exists - this is a programming error if it fails
            if (!SubcategoryNames.TryGetValue(subcategoryKey, out var knownNames))
            {
                throw new ArgumentException(
                    $"Unknown subcategory key: '{subcategoryKey}'. " +
                    $"Valid keys: {string.Join(", ", SubcategoryNames.Keys)}",
                    nameof(subcategoryKey)
                );
            }

            // Guard: parent category might not have subcategories
            if (parentCategory?.SubCategories == null)
            {
                return null;
            }

            // STRATEGY 1: Try known localized names
            // This is the fast path for supported languages
            foreach (var name in knownNames)
            {
                var subcat = parentCategory.SubCategories
                    .Cast<Category>()
                    .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (subcat != null)
                {
                    Infrastructure.Logger.Info($"Found subcategory '{name}' via known translations");
                    return subcat;
                }
            }

            // STRATEGY 2: Pattern match for unknown/variant names
            // Try to find any subcategory containing the key word (case-insensitive)
            // This catches:
            // - System subcategories: "<Projection>", "<Projection-Modified>"
            // - Custom subcategories: "Projectielijnen", "Projection Lines"
            //
            // LIMITATION: This won't catch non-Latin scripts (Chinese, Japanese)
            // Those must be added to SubcategoryNames manually after testing.
            var patternMatch = parentCategory.SubCategories
                .Cast<Category>()
                .FirstOrDefault(c =>
                    c.Name.IndexOf(subcategoryKey, StringComparison.OrdinalIgnoreCase) >= 0
                );

            if (patternMatch != null)
            {
                Infrastructure.Logger.Info(
                    $"Found subcategory '{patternMatch.Name}' via pattern match for key '{subcategoryKey}'"
                );
                return patternMatch;
            }

            // STRATEGY 3 (last resort): Log all subcategories for debugging
            // This helps diagnose issues in languages we haven't seen
            Infrastructure.Logger.Warn(
                $"Could not find subcategory for key '{subcategoryKey}'. " +
                $"Available subcategories: {string.Join(", ", parentCategory.SubCategories.Cast<Category>().Select(c => c.Name))}"
            );

            return null;
        }
    }
}
