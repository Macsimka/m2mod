#pragma once
#include <cstdint>
#include <string>

uint64_t CalcJenkinsHash(const void* data, size_t nLength);

namespace M2Lib
{
	template <class T>
	uint64_t CalcStringHash(std::basic_string<T> szFileName);

	template <class T>
	std::basic_string<T> NormalizePath(std::basic_string<T> const& path);

	template <class T>
	std::basic_string<T> ToLower(std::basic_string<T> str);
}
