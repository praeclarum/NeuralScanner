
CONFIGURATION=Release
PLATFORM=iPhone

all:

restore:
	msbuild /t:Restore /p:Configuration=$(CONFIGURATION) /p:Platform=$(PLATFORM) NeuralScanner.sln

release: restore
	@echo "Building release"
	msbuild /t:Build /p:Configuration=$(CONFIGURATION) /p:Platform=$(PLATFORM) /p:ArchiveOnBuild=true /p:BuildIpa=true /p:EnableCodeSigning=true "/p:CodesignKey=Apple Distribution" NeuralScanner.sln
	ls -al NeuralScanner/bin/iPhone/Release/NeuralScanner.ipa
