#pragma once

#include <algorithm>
#include <cwctype>
#include <string>
#include <iterator>
#include <unordered_map>
#include <vector>

namespace enput {

using CandidateFrequencyMap = std::unordered_map<std::wstring, unsigned int>;

inline std::wstring CandidateFrequencyKey(const std::wstring& candidate) {
    std::wstring key = candidate;
    std::transform(key.begin(), key.end(), key.begin(), [](wchar_t character) { return static_cast<wchar_t>(towlower(character)); });
    return key;
}

inline void RankCandidatesByFrequency(std::vector<std::wstring>* candidates, const CandidateFrequencyMap& frequencies, bool enabled = true) {
    if (!enabled || !candidates || candidates->size() < 2 || frequencies.empty()) return;

    struct RankedCandidate {
        std::wstring text;
        unsigned int frequency = 0;
    };

    std::vector<RankedCandidate> selected;
    std::vector<std::wstring> remaining;
    selected.reserve(candidates->size());
    remaining.reserve(candidates->size());
    for (const std::wstring& candidate : *candidates) {
        const auto frequency = frequencies.find(CandidateFrequencyKey(candidate));
        if (frequency != frequencies.end() && frequency->second > 0) selected.push_back({ candidate, frequency->second });
        else remaining.push_back(candidate);
    }

    std::stable_sort(selected.begin(), selected.end(), [](const RankedCandidate& left, const RankedCandidate& right) {
        return left.frequency > right.frequency;
    });

    candidates->clear();
    candidates->reserve(selected.size() + remaining.size());
    for (RankedCandidate& candidate : selected) candidates->push_back(std::move(candidate.text));
    candidates->insert(candidates->end(), std::make_move_iterator(remaining.begin()), std::make_move_iterator(remaining.end()));
}

} // namespace enput
