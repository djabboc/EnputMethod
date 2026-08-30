#include "../EnputMethod.Tsf/CandidateRanking.h"
#include "../EnputMethod.Tsf/CandidateSelection.h"
#include "../EnputMethod.Tsf/ApproximateMatch.h"
#include "../EnputMethod.Tsf/JsonObjectReader.h"
#include "../EnputMethod.Tsf/SuggestionCancellation.h"
#include "../EnputMethod.Tsf/TranslationText.h"

#include <cstdlib>
#include <iostream>

namespace {

void Expect(bool condition, const char* message) {
    if (condition) return;
    std::cerr << "FAILED: " << message << '\n';
    std::exit(1);
}

void ExpectIndex(WPARAM key, int expected, const char* message) {
    Expect(enput::CandidateIndex(key) == expected, message);
}

void VerifyDigitMappings() {
    for (int index = 0; index < 9; ++index) {
        ExpectIndex(static_cast<WPARAM>('1' + index), index, "Top-row digit did not map to its candidate index.");
        ExpectIndex(static_cast<WPARAM>(VK_NUMPAD1 + index), index, "Numpad digit did not map to its candidate index.");
    }
    ExpectIndex('0', -1, "Zero must not select a candidate.");
    ExpectIndex('A', -1, "Letters must not select a candidate.");
    ExpectIndex(VK_F1, -1, "Function keys must not select a candidate.");
}

void VerifyCandidateBounds() {
    for (std::size_t candidateCount = 1; candidateCount <= 9; ++candidateCount) {
        std::size_t index = 99;
        Expect(enput::TryGetCandidateIndex(static_cast<WPARAM>('1' + candidateCount - 1), candidateCount, &index), "Last visible candidate must be selectable.");
        Expect(index == candidateCount - 1, "Selected candidate index did not match the visible candidate.");
        if (candidateCount < 9) {
            Expect(!enput::TryGetCandidateIndex(static_cast<WPARAM>('1' + candidateCount), candidateCount, &index), "A digit beyond the visible candidate count must pass through.");
        }
    }
    Expect(!enput::TryGetCandidateIndex('1', 0, nullptr), "No digit can select when no candidates are visible.");
}

void VerifyEmojiJsonEscapes() {
    enput::json::Value document;
    Expect(enput::json::ReadDocument(R"({"emoji":"\uD83D\uDD25"})", &document), "Emoji JSON with a surrogate pair must parse.");
    const enput::json::Value* emoji = enput::json::ObjectValue(document, "emoji");
    Expect(emoji && emoji->type == enput::json::Value::Type::String && emoji->string == "\xF0\x9F\x94\xA5", "A JSON surrogate pair must decode to the UTF-8 fire Emoji.");

    Expect(enput::json::ReadDocument(R"({"emoji":"\uD83E\uDE9A"})", &document), "Saw Emoji JSON must parse.");
    emoji = enput::json::ObjectValue(document, "emoji");
    Expect(emoji && emoji->type == enput::json::Value::Type::String && emoji->string == "\xF0\x9F\xAA\x9A", "Saw must preserve Unicode U+1FA9A when it is committed to an editor.");

    Expect(enput::json::ReadDocument(R"({"emoji":"\uD83C\uDDF8\uD83C\uDDED"})", &document), "Saint Helena flag JSON must parse.");
    emoji = enput::json::ObjectValue(document, "emoji");
    Expect(emoji && emoji->type == enput::json::Value::Type::String && emoji->string == "\xF0\x9F\x87\xB8\xF0\x9F\x87\xAD", "Saint Helena must preserve regional indicators S and H, not Switzerland C and H.");
}

void VerifyTranslationLineBreaks() {
    const std::wstring normalized = enput::NormalizeEscapedLineBreaks(L"First\\nSecond\\r\\nThird\\rFourth");
    Expect(normalized == L"First\nSecond\nThird\nFourth", "Escaped dictionary line breaks must render as actual lines.");
}
void VerifyFrequencyRanking() {
    std::vector<std::wstring> candidates{ L"hello", L"help", L"helium", L"hero" };
    enput::CandidateFrequencyMap frequencies;
    frequencies[enput::CandidateFrequencyKey(L"HELP")] = 7;
    frequencies[enput::CandidateFrequencyKey(L"hero")] = 3;
    enput::RankCandidatesByFrequency(&candidates, frequencies);
    Expect(candidates[0] == L"help", "Most frequently selected candidate must be first.");
    Expect(candidates[1] == L"hero", "Second-most frequently selected candidate must follow.");
    Expect(candidates[2] == L"hello", "Unselected candidates must keep dictionary order.");
    Expect(candidates[3] == L"helium", "Unselected candidates must keep dictionary order.");
    std::vector<std::wstring> disabledCandidates{ L"hello", L"help", L"helium", L"hero" };
    enput::RankCandidatesByFrequency(&disabledCandidates, frequencies, false);
    Expect(disabledCandidates == std::vector<std::wstring>{ L"hello", L"help", L"helium", L"hero" }, "Disabled frequency ranking must preserve dictionary order.");
}
void VerifyTranslationTextNormalization() {
    const std::wstring normalized = enput::NormalizeDictionaryText(L"n a brace\n{{or}} a support\nvt hold steady");
    Expect(normalized == L"n. a brace\nor a support\nvt. hold steady", "Dictionary markup must be cleaned while POS punctuation is normalized.");
    Expect(enput::NormalizeDictionaryText(L"n. already punctuated") == L"n. already punctuated", "Existing POS punctuation must not be removed or duplicated.");
}

void VerifyOrderedSubsequenceMatching() {
    Expect(enput::IsOrderedSubsequence(L"hpy", L"happy"), "Ordered incomplete input must match happy.");
    Expect(enput::IsOrderedSubsequence(L"pignose", L"pig_nose", true), "Emoji keyword separators must be ignored for ordered matching.");
    Expect(enput::IsOrderedCompactSubsequence(L"empirestate", L"Empire State Building"), "Space-free phrase input must match its spaced phrase candidate.");
    Expect(enput::IsOrderedCompactSubsequence(L"newyork", L"new york"), "Space-free place-name input must match its phrase candidate.");
    Expect(enput::IsOrderedCompactSubsequence(L"machinelearning", L"machine learning"), "Space-free academic phrase input must match its phrase candidate.");
    Expect(enput::IsOrderedSubsequence(L"pno", L"pig_nose", true), "Emoji ordered matching must preserve non-adjacent characters.");
    Expect(!enput::IsOrderedSubsequence(L"pyh", L"happy"), "Ordered matching must reject reversed character order.");
    Expect(enput::LikeOrderedSubsequencePattern(L"hpy") == L"%h%p%y%", "SQLite LIKE fallback pattern must preserve order.");
}

void VerifyDetachedSuggestionCancellation() {
    const std::vector<WPARAM> cancelShortcuts{ VK_ESCAPE, VK_SHIFT };
    Expect(enput::HasConfiguredShortcut(cancelShortcuts, VK_ESCAPE), "Escape must remain a configured cancellation shortcut.");
    Expect(enput::HasConfiguredShortcut(cancelShortcuts, VK_SHIFT), "Generic Shift must remain a configured cancellation shortcut.");
    Expect(enput::HasConfiguredShortcut(cancelShortcuts, VK_LSHIFT), "Left Shift must honor a generic Shift cancellation shortcut.");
    Expect(enput::HasConfiguredShortcut(cancelShortcuts, VK_RSHIFT), "Right Shift must honor a generic Shift cancellation shortcut.");
    Expect(enput::ResolveCandidateCancellationAction(true, false, true) == enput::CandidateCancellationAction::DismissDetachedSuggestion,
           "Escape or Shift must dismiss the detached world suggestion after hello is committed.");
    Expect(enput::ResolveCandidateCancellationAction(false, false, false) == enput::CandidateCancellationAction::FinishComposition,
           "Active composition cancellation must keep its existing finish path.");
    Expect(enput::ResolveCandidateCancellationAction(false, true, true) == enput::CandidateCancellationAction::ExitEmptyEmojiMode,
           "Empty Emoji mode cancellation must keep its existing exit behavior.");
}

}

int main() {
    VerifyDigitMappings();
    VerifyEmojiJsonEscapes();
    VerifyTranslationLineBreaks();
    VerifyTranslationTextNormalization();
    VerifyOrderedSubsequenceMatching();
    VerifyDetachedSuggestionCancellation();
    VerifyFrequencyRanking();
    VerifyCandidateBounds();
    std::cout << "TSF candidate selection tests passed.\n";
    return 0;
}
