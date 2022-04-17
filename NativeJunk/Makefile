CXX=clang++
CXXFLAGS=-std=c++17 -stdlib=libc++ -frtti -fexceptions \
	-ftemplate-depth=1024 -Qunused-arguments \
	-fPIC -O3 -Wall -Werror \
	-lstdc++ \
	-IOpenGR/src \
	-Wno-int-in-bool-context -Wno-unused-local-typedef \
	-Wno-invalid-offsetof -Wno-error=unused-variable -Wno-uninitialized-const-reference -Wno-unnamed-type-template-args

CPPSRCS=
MMSRCS=NativeJunk.mm
SRCS=$(CPPSRCS) $(MMSRCS)

CPPOBJS=$(CPPSRCS:.cpp=.o)
MMOBJS=$(MMSRCS:.mm=.o)
OBJS=$(CPPOBJS) $(MMOBJS)

OUTDIR=lib
OBJDIR=obj

IOS_SYSROOT=$(shell xcrun --sdk iphoneos --show-sdk-path)
MACCAT_SYSROOT=$(shell xcrun --sdk macosx --show-sdk-path)
IOSSIM_SYSROOT=$(shell xcrun --sdk iphonesimulator --show-sdk-path)

all: $(OUTDIR)/libNativeJunk-ios.a $(OUTDIR)/libNativeJunk-maccat.a

$(OBJDIR)/libNativeJunk-arm64-apple-ios10.0.a: $(SRCS)
	$(CXX) $(CXXFLAGS) -isysroot "$(IOS_SYSROOT)" -target arm64-apple-ios10.0 $(SRCS) -c
	$(AR) -rcs $@ $(OBJS)
	rm $(OBJS)

$(OBJDIR)/libNativeJunk-x86_64-apple-iossim10.0.a: $(SRCS)
	$(CXX) $(CXXFLAGS) -isysroot "$(IOSSIM_SYSROOT)" -target x86_64-apple-iossim10.0 $(SRCS) -c
	$(AR) -rcs $@ $(OBJS)
	rm $(OBJS)

$(OBJDIR)/libNativeJunk-x86_64-apple-ios13.1-macabi.a: $(SRCS)
	$(CXX) $(CXXFLAGS) -isysroot "$(MACCAT_SYSROOT)" -target x86_64-apple-ios13.1-macabi $(SRCS) -c
	$(AR) -rcs $@ $(OBJS)
	rm $(OBJS)

$(OBJDIR)/libNativeJunk-arm64-apple-ios13.1-macabi.a: $(SRCS)
	$(CXX) $(CXXFLAGS) -isysroot "$(MACCAT_SYSROOT)" -target arm64-apple-ios13.1-macabi $(SRCS) -c
	$(AR) -rcs $@ $(OBJS)
	rm $(OBJS)

$(OUTDIR)/libNativeJunk-maccat.a: $(OBJDIR)/libNativeJunk-x86_64-apple-ios13.1-macabi.a $(OBJDIR)/libNativeJunk-arm64-apple-ios13.1-macabi.a
	lipo -output $@ -create $^

$(OUTDIR)/libNativeJunk-ios.a: $(OBJDIR)/libNativeJunk-x86_64-apple-iossim10.0.a $(OBJDIR)/libNativeJunk-arm64-apple-ios10.0.a
	lipo -output $@ -create $^
