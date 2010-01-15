include $(top_srcdir)/Makefile.include

CSFLAGS = 		\
	-debug 		\
	-define:DEBUG 	\
	-target:library

ASSEMBLIES = 					\
	$(LINK_TOMBOY_EXE)			\
	$(GTKSHARP_LIBS) 				\
	$(LINK_MONO_ADDINS)			\
	-r:Mono.Posix

#
# Evernote System Sync Service Addin
#

TARGET = EvernoteSyncService.dll
CSFILES = \
	$(srcdir)/EvernoteSyncServiceAddin.cs
	$(srcdir)/EvernoteSyncServer.cs
RESOURCES = \
	-resource:$(srcdir)/EvernoteSyncService.addin.xml

$(TARGET).mdb: $(TARGET)

$(TARGET): $(CSFILES) $(top_builddir)/Tomboy/Tomboy.exe
	$(CSC) -out:$@ $(CSFLAGS) $(ASSEMBLIES) $(CSFILES) $(RESOURCES)


addinsdir = $(pkglibdir)/addins
addins_DATA = 			\
	$(TARGET)		\
	$(TARGET).mdb

EXTRA_DIST =            	\
	$(CSFILES) \
	$(srcdir)/EvernoteSyncService.addin.xml

CLEANFILES =				\
	$(TARGET).mdb \
	$(TARGET)

