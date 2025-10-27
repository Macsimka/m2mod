#include "Settings.h"

void M2Lib::Settings::setOutputDirectory(const char* directory)
{
	strcpy_s(OutputDirectory, std::size(OutputDirectory), directory);
}

void M2Lib::Settings::setWorkingDirectory(const char* directory)
{
	strcpy_s(WorkingDirectory, std::size(WorkingDirectory), directory);
}

void M2Lib::Settings::setMappingsDirectory(const char* directory)
{
	strcpy_s(MappingsDirectory, std::size(MappingsDirectory), directory);
}

void M2Lib::Settings::operator=(Settings const& other)
{
	setOutputDirectory(other.OutputDirectory);
	setWorkingDirectory(other.WorkingDirectory);
	setMappingsDirectory(other.MappingsDirectory);
	ForceLoadExpansion = other.ForceLoadExpansion;
	MergeBones = other.MergeBones;
	MergeAttachments = other.MergeAttachments;
	MergeCameras = other.MergeCameras;
	FixSeams = other.FixSeams;
	FixEdgeNormals = other.FixEdgeNormals;
	IgnoreOriginalMeshIndexes = other.IgnoreOriginalMeshIndexes;
	FixAnimationsTest = other.FixAnimationsTest;
	CustomFilesStartIndex = other.CustomFilesStartIndex;
}
