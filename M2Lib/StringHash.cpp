#include "StringHash.h"
#include "lookup.h"
#include <algorithm>

uint64_t CalcJenkinsHash(const void* data, size_t nLength)
{
	uint32_t dwHashHigh = 0;
	uint32_t dwHashLow = 0;

	hashlittle2(data, nLength, &dwHashHigh, &dwHashLow);
	return ((uint64_t)dwHashHigh << 0x20) | dwHashLow;
}

template <class T>
uint64_t M2Lib::CalcStringHash(std::basic_string<T> path)
{
	std::ranges::transform(path, path.begin(), [&](T c)
	{
		return ::toupper(normalize_char<T>(c));
	});

	return CalcJenkinsHash(path.c_str(), path.length() * sizeof(T));
}

template
uint64_t M2Lib::CalcStringHash(std::basic_string<char> szFileName);

template
uint64_t M2Lib::CalcStringHash(std::basic_string<wchar_t> szFileName);

template <class T>
T normalize_char(T value) { return value; }

template <>
char normalize_char(char value)
{
	if (value == '\\')
		return '/';

	return tolower(value);
}

template <>
wchar_t normalize_char(wchar_t value)
{
	if (value == L'\\')
		return L'/';

	return towlower(value);
}

template <class T>
std::basic_string<T> M2Lib::NormalizePath(std::basic_string<T> const& path)
{
	auto copy = path;

	std::ranges::transform(copy, copy.begin(), normalize_char<T>);

	return copy;
}

template
std::basic_string<char> M2Lib::NormalizePath(std::basic_string<char> const& path);

template
std::basic_string<wchar_t> M2Lib::NormalizePath(std::basic_string<wchar_t> const& path);

template <>
std::basic_string<char> M2Lib::ToLower(std::basic_string<char> str)
{
	std::ranges::transform(str, str.begin(), tolower);
	return str;
}

template <>
std::basic_string<wchar_t> M2Lib::ToLower(std::basic_string<wchar_t> str)
{
	std::ranges::transform(str, str.begin(), towlower);
	return str;
}
