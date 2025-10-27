#pragma once

#include "BaseTypes.h"
#include "M2Types.h"

namespace M2Lib
{
#pragma pack(push, 1)
	struct M2LIB_API_CLASS Settings
	{
		char OutputDirectory[1024];
		char WorkingDirectory[1024];
		char MappingsDirectory[1024];
		Expansion ForceLoadExpansion = Expansion::None;
		uint32_t CustomFilesStartIndex = 0;
		bool MergeBones = true;
		bool MergeAttachments = true;
		bool MergeCameras = true;
		bool FixSeams = false;
		bool FixEdgeNormals = true;
		bool IgnoreOriginalMeshIndexes = false;
		bool FixAnimationsTest = false;

		void setOutputDirectory(const char* directory);
		void setWorkingDirectory(const char* directory);
		void setMappingsDirectory(const char* directory);

		Settings()
		{
			setOutputDirectory("");
			setWorkingDirectory("");
			setMappingsDirectory("");
		}

		void operator=(Settings const& other);
	};

	ASSERT_SIZE(Settings, 1024 * 3 + 4 + 4 + 7);
#pragma pack(pop)
}
