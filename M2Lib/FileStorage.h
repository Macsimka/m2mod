#pragma once

#include "BaseTypes.h"
#include <string>
#include <map>
#include <unordered_map>

namespace std {
	namespace filesystem {
		class path;
	}
}

namespace M2Lib
{
	struct FileInfo
	{
		FileInfo(uint32_t FileDataId, const char* Path);

		uint32_t FileDataId = 0;
		std::string Path;
	};

	class FileStorage
	{
		bool loadFailed = false;
		uint32_t MaxFileDataId = 0;
		void ClearStorage();
		bool LoadMappings();

		std::unordered_map<uint32_t, FileInfo const*> fileInfosByFileDataId;
		std::unordered_map<uint64_t, FileInfo const*> fileInfosByNameHash;
		std::string mappingsDirectory;

		bool ParseCsv(std::string const& Path);

	public:
		FileStorage(std::string const& mappingsDirectory);
		void SetMappingsDirectory(std::string const& mappingsDirectory);
		void AddRecord(FileInfo const* record);
		static std::filesystem::path DetectWorkingDirectory(std::filesystem::path fullPath, std::filesystem::path relativePath);

		~FileStorage();

		bool LoadStorage();
		void ResetLoadFailed();

		bool Loaded() const { return GetStorageSize() > 0; }
		uint32_t GetStorageSize() const { return fileInfosByFileDataId.size(); }
		uint32_t GetMaxFileDataId() const { return MaxFileDataId; }

		FileInfo const* GetFileInfoByPartialPath(std::string const& Name);
		FileInfo const* GetFileInfoByFileDataId(uint32_t FileDataId);
		FileInfo const* GetFileInfoByPath(std::string const& Path);
		char const* PathInfo(uint32_t FileDataId);

		static const std::string DefaultMappingsPath;
	};

	class StorageManager
	{
	private:
		std::unordered_map<uint64_t, FileStorage*> storages;

	public:

		static StorageManager* GetInstance()
		{
			static StorageManager instance;

			return &instance;
		}

		~StorageManager();

		FileStorage* GetStorage(std::string const& mappingDirectory);
		void Clear();
	};

	M2LIB_API M2LIB_HANDLE __cdecl FileStorage_Get(const char* mappingsDirectory);
	M2LIB_API void __cdecl FileStorage_Clear();
	M2LIB_API void __cdecl FileStorage_SetMappingsDirectory(M2LIB_HANDLE handle, const char* mappingsDirectory);
	M2LIB_API M2LIB_HANDLE __cdecl FileStorage_GetFileInfoByFileDataId(M2LIB_HANDLE handle, uint32_t FileDataId);
	M2LIB_API M2LIB_HANDLE __cdecl FileStorage_GetFileInfoByPartialPath(M2LIB_HANDLE handle, char const* Path);

	M2LIB_API uint32_t __cdecl FileInfo_GetFileDataId(M2LIB_HANDLE handle);
	M2LIB_API char const* __cdecl FileInfo_GetPath(M2LIB_HANDLE handle);
}
