#include "FileStorage.h"
#include "Logger.h"
#include "StringHelpers.h"
#include "StringHash.h"
#include <fstream>
#include <algorithm>
#include <filesystem>
#include <locale>
#include <charconv>

const std::string M2Lib::FileStorage::DefaultMappingsPath = (std::filesystem::current_path() / "mappings").string();

M2Lib::FileInfo::FileInfo(uint32_t FileDataId, const char* Path)
{
	this->FileDataId = FileDataId;
	this->Path = NormalizePath<char>(Path);
}

void M2Lib::FileStorage::ClearStorage()
{
	loadFailed = false;
	for (auto& info : fileInfosByFileDataId)
		delete info.second;
	fileInfosByFileDataId.clear();
	fileInfosByNameHash.clear();
	MaxFileDataId = 0;
}

uint32_t M2Lib::FileInfo_GetFileDataId(M2LIB_HANDLE handle)
{
	return static_cast<FileInfo*>(handle)->FileDataId;
}

char const* M2Lib::FileInfo_GetPath(M2LIB_HANDLE handle)
{
	return static_cast<FileInfo*>(handle)->Path.c_str();
}

bool M2Lib::FileStorage::ParseCsv(std::string const& Path)
{
	std::ifstream in(Path, std::ios::in);
	if (!in)
		return false;

	fileInfosByNameHash.reserve(2000000);
	fileInfosByFileDataId.reserve(2000000);

	sLogger.LogInfo("Hello from ParseCsv");

	auto startTime = std::chrono::high_resolution_clock::now();

	std::string line;
	line.reserve(128);

	while (std::getline(in, line))
	{
		// Skip empty lines
		if (line.empty())
			continue;

		auto colonPos = line.find(';');
		if (colonPos == std::string::npos)
			continue;

		// Parse FileDataId using from_chars
		uint32_t FileDataId = 0;
		auto [ptr, ec] = std::from_chars(line.data(), line.data() + colonPos, FileDataId);
		if (ec != std::errc())
			continue;

		// Get filename part
		const char* fileName = line.data() + colonPos + 1;
		size_t fileNameLen = line.length() - colonPos - 1;

		// Remove trailing \r\n
		while (fileNameLen > 0 && (fileName[fileNameLen - 1] == '\r' || fileName[fileNameLen - 1] == '\n'))
			--fileNameLen;

		if (fileNameLen == 0)
			continue;

		// Try to emplace FileDataId (one search operation instead of find + insert)
		auto [itr1, inserted1] = fileInfosByFileDataId.try_emplace(FileDataId, nullptr);
		if (!inserted1)
		{
			sLogger.LogWarning("Duplicate file storage entry '%u':'%.*s' (already used: '%u':'%s'), skipping",
				FileDataId, static_cast<int>(fileNameLen), fileName, itr1->second->FileDataId, itr1->second->Path.c_str());
			continue;
		}

		// Create string only after successful FileDataId check
		auto nameHash = CalcStringHash<char>(fileName);

		// Try to emplace nameHash (one search operation instead of find + insert)
		auto [itr2, inserted2] = fileInfosByNameHash.try_emplace(nameHash, nullptr);
		if (!inserted2)
		{
			sLogger.LogWarning("Duplicate file storage entry '%u':'%.*s' (already used: '%u':'%s')",
				FileDataId, static_cast<int>(fileNameLen), fileName, itr2->second->FileDataId, itr2->second->Path.c_str());
			// Rollback FileDataId insertion
			fileInfosByFileDataId.erase(itr1);
			continue;
		}

		// Create FileInfo and assign to both maps
		auto info = new FileInfo(FileDataId, fileName);
		if (MaxFileDataId < FileDataId)
			MaxFileDataId = FileDataId;

		itr1->second = info;
		itr2->second = info;
	}

	in.close();

	auto endTime = std::chrono::high_resolution_clock::now();
	auto durationMs = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime).count();
	sLogger.LogInfo("ParseCsv completed in %lld ms", durationMs);

	return true;
}

bool M2Lib::FileStorage::LoadStorage()
{
	if (!fileInfosByFileDataId.empty())
		return true;

	if (!LoadMappings()) {
		loadFailed = true;

		return false;
	}

	sLogger.LogInfo("Loaded %u mapping entries", fileInfosByFileDataId.size());

	return true;
}

void M2Lib::FileStorage::ResetLoadFailed()
{
	loadFailed = false;
}

M2Lib::FileStorage::FileStorage(std::string const& mappingsDirectory)
{
	SetMappingsDirectory(mappingsDirectory);
}

void M2Lib::FileStorage::SetMappingsDirectory(std::string const& mappingsDirectory)
{
	this->mappingsDirectory = mappingsDirectory;
	ClearStorage();
}

void M2Lib::FileStorage::AddRecord(FileInfo const* record)
{
	fileInfosByFileDataId[record->FileDataId] = record;
	fileInfosByNameHash[CalcStringHash(record->Path)] = record;

	if (record->FileDataId > MaxFileDataId)
		MaxFileDataId = record->FileDataId;
}

M2Lib::FileStorage::~FileStorage()
{
	ClearStorage();
}

bool M2Lib::FileStorage::LoadMappings()
{
	if (loadFailed)
		return false;

	auto directory = mappingsDirectory.length() > 0 ? mappingsDirectory : DefaultMappingsPath;
	if (!std::filesystem::is_directory(directory)) {
		sLogger.LogWarning("Mappings directory '%s' does not exist", directory.c_str());
		return false;
	}

	sLogger.LogInfo("Loading mappings at '%s'", directory.c_str());

	std::filesystem::directory_iterator itr;

	const auto isSupportedExtension = [](std::string const& extension)
	{
		std::string copy = extension;
		std::ranges::transform(copy, copy.begin(), ::tolower);
		return copy == ".csv" || copy == ".txt";
	};

	auto now = time(NULL);
	for (auto& p : std::filesystem::directory_iterator(directory))
	{
		if (!isSupportedExtension(p.path().extension().string()))
			continue;

		sLogger.LogInfo("Loading mapping '%s'", p.path().filename().string().c_str());

		try
		{
			if (!ParseCsv(p.path().string()))
				sLogger.LogError("Failed to parse mapping file '%s'", p.path().filename().c_str());
		}
		catch (std::exception& e)
		{
			sLogger.LogError("Failed to parse mapping file '%s': %s", p.path().filename().c_str(), e.what());
		}
	}

	return true;
}

M2Lib::FileInfo const* M2Lib::FileStorage::GetFileInfoByPartialPath(std::string const & Name)
{
	LoadStorage();

	const auto NameCopy = NormalizePath(Name);

	for (auto& itr : fileInfosByFileDataId)
	{
		if (NormalizePath(itr.second->Path).find(NameCopy) != std::string::npos)
			return itr.second;
	}

	return nullptr;
}

M2Lib::FileInfo const* M2Lib::FileStorage::GetFileInfoByFileDataId(uint32_t FileDataId)
{
	LoadStorage();

	auto itr = fileInfosByFileDataId.find(FileDataId);
	if (itr == fileInfosByFileDataId.end())
		return nullptr;

	return itr->second;
}

M2Lib::FileInfo const* M2Lib::FileStorage::GetFileInfoByPath(std::string const& Path)
{
	LoadStorage();

	uint64_t hash = CalcStringHash(Path);
	auto itr = fileInfosByNameHash.find(hash);
	if (itr != fileInfosByNameHash.end())
		return itr->second;

	return nullptr;
}

char const* M2Lib::FileStorage::PathInfo(uint32_t FileDataId)
{
	if (!FileDataId)
		return "<none>";

	auto info = GetFileInfoByFileDataId(FileDataId);
	if (!info)
		return "<not found in listfile>";

	return info->Path.c_str();
}

std::filesystem::path M2Lib::FileStorage::DetectWorkingDirectory(std::filesystem::path fullPath, std::filesystem::path relativePath)
{
	for (;;)
	{
		if (relativePath.empty())
			return fullPath;

		if (ToLower(relativePath.filename().string()) == ToLower(fullPath.filename().string()))
		{
			relativePath = relativePath.parent_path();
			fullPath = fullPath.parent_path();
		}
		else
			return "";
	}
}

M2Lib::FileStorage* M2Lib::StorageManager::GetStorage(std::string const& mappingDirectory)
{
	const auto hash = CalcStringHash(mappingDirectory);
	auto itr = storages.find(hash);
	if (itr != storages.end()) {

		itr->second->ResetLoadFailed();
		return itr->second;
	}

	auto storage = new FileStorage(mappingDirectory);
	storages[hash] = storage;

	return storage;
}

void M2Lib::StorageManager::Clear()
{
	for (auto storage : storages)
		delete storage.second;

	storages.clear();
}

M2Lib::StorageManager::~StorageManager()
{
	Clear();
}

M2LIB_HANDLE M2Lib::FileStorage_Get(const char* mappingsDirectory)
{
	return static_cast<M2LIB_HANDLE>(StorageManager::GetInstance()->GetStorage(mappingsDirectory));
}

void M2Lib::FileStorage_Clear()
{
	StorageManager::GetInstance()->Clear();
};

void M2Lib::FileStorage_SetMappingsDirectory(M2LIB_HANDLE handle, const char* mappingsDirectory)
{
	static_cast<FileStorage*>(handle)->SetMappingsDirectory(mappingsDirectory);
}

M2LIB_HANDLE M2Lib::FileStorage_GetFileInfoByFileDataId(M2LIB_HANDLE handle, uint32_t FileDataId)
{
	return (M2LIB_HANDLE)static_cast<FileStorage*>(handle)->GetFileInfoByFileDataId(FileDataId);
}

M2LIB_HANDLE M2Lib::FileStorage_GetFileInfoByPartialPath(M2LIB_HANDLE handle, char const* Path)
{
	return (M2LIB_HANDLE)static_cast<FileStorage*>(handle)->GetFileInfoByPartialPath(Path);
}
