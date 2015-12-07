using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

/* TODO:
 * flip texture cordinates
 * decompress zlibbed sections even though they are not used
 * calculate triangle strips with implicit indices
 * calculate vertex arrays with encoding 1
 */

namespace M3G2FBX
{
    class Program
    {
        static byte[] JSR184 = new byte[12] { 0xAB, 0x4A, 0x53, 0x52, 0x31, 0x38, 0x34, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        static byte[] IM2M3G = new byte[12] { 0xAB, 0x49, 0x4D, 0x32, 0x4D, 0x33, 0x47, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        static byte[] IM3M3G = new byte[12] { 0xAB, 0x49, 0x4D, 0x33, 0x4D, 0x33, 0x47, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };

        static string currentPath = AppDomain.CurrentDomain.BaseDirectory;
        static int version = 0;
        static byte[] FileIdentifier = new byte[12];
        static bool makeUnique = false;

        public class M3Gobject
        {
            public byte type;
            public int length;
            public long offset;
            public bool connected;
            public int[] appearance;
        }

        static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            SearchOption recurse = SearchOption.AllDirectories;

            string input = "";
            int totalConverted = 0;

            switch (args.Length)
            {
                case 0:
                    input = AppDomain.CurrentDomain.BaseDirectory;
                    recurse = SearchOption.TopDirectoryOnly;
                    break;
                case 1:
                    if (args[0] == "-u")
                    {
                        makeUnique = true;
                        goto case 0;
                    }
                    else { input = args[0]; }
                    break;
                case 2:
                    if (args[0] == "-u")
                    {
                        makeUnique = true;
                        input = args[1];
                    }
                    break;
                default:
                    Console.WriteLine("M3G to FBX model converter\nby Chipicao - www.KotsChopShop.com\n\nUsage: M3G2FBX.exe [-u] [input_file/folder]");
                    break;
            }

            if (File.Exists(input))
            {
                //in case only filename is entered
                //input = Path.GetFullPath(input) + "\\" + Path.GetFileName(input);
                input = Path.GetFullPath(input);
                currentPath = Path.GetDirectoryName(input) + "\\";
                ReadM3G(input);
                Console.WriteLine("Finished.");
            }
            else if (Directory.Exists(input))
            {
                input = Path.GetFullPath(input);
                string[] inputFiles = Directory.GetFiles(input, "*.m3g", recurse);
                foreach (var inputFile in inputFiles)
                {
                    currentPath = Path.GetDirectoryName(inputFile) + "\\";
                    totalConverted += ReadM3G(inputFile);
                }
                Console.WriteLine("Finished converting {0} files.", totalConverted);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            else { Console.WriteLine("Invalid input file/folder: {0}\n\nUsage: M3G2FBX.exe [-u] [input_file/folder]", input); }
        }

        private static int ReadM3G(string M3Gfile)
        {
            string FBXfile = Path.GetDirectoryName(M3Gfile) + "\\" + Path.GetFileNameWithoutExtension(M3Gfile) + ".fbx";
            if (File.Exists(FBXfile))
            {
                Console.WriteLine("File already converted: " + Path.GetFileName(FBXfile));
                return 0;
            }
            else
            {
                Console.WriteLine("Converting " + Path.GetFileName(M3Gfile));
                //byte[] FileIdentifier = new byte[12];
                int converted = 0;

                using (FileStream inStream = File.OpenRead(M3Gfile))
                {
                    inStream.Read(FileIdentifier, 0, 12);

                    if (FileIdentifier[0] == 0x1F && FileIdentifier[1] == 0x8C && FileIdentifier[6] == 0x1F && FileIdentifier[7] == 0x8B) //gzip
                    {
                        int uncompressedSize = BitConverter.ToInt32(new byte[4] { FileIdentifier[2], FileIdentifier[3], FileIdentifier[4], FileIdentifier[5] }, 0);
                        byte[] decompressionBuffer = new byte[uncompressedSize];

                        int gzipSize = (int)inStream.Length - 6;
                        byte[] gzipbuffer = new byte[gzipSize];
                        inStream.Position = 6;
                        inStream.Read(gzipbuffer, 0, gzipSize);

                        using (MemoryStream gzipStream = new MemoryStream(gzipbuffer))
                        using (GZipStream compressedStream = new GZipStream(gzipStream, CompressionMode.Decompress))
                        using (MemoryStream decompressedStream = new MemoryStream(decompressionBuffer))
                        {
                            compressedStream.Read(decompressionBuffer, 0, uncompressedSize); //so this still applies to decompressedStream; nice!
                            decompressedStream.Read(FileIdentifier, 0, 12);
                            if (FileIdentifier.SequenceEqual(JSR184)) { version = 1; }
                            else if (FileIdentifier.SequenceEqual(IM2M3G)) { version = 2; }
                            else if (FileIdentifier.SequenceEqual(IM3M3G)) { version = 3; }
                            else
                            {
                                Console.WriteLine("Unsupported file type: " + Path.GetFileName(M3Gfile));
                                return 0;
                            }

                            converted = ConvertM3G(decompressedStream, FBXfile);
                        }
                    }
                    else
                    {
                        if (FileIdentifier.SequenceEqual(JSR184)) { version = 1; }
                        else if (FileIdentifier.SequenceEqual(IM2M3G)) { version = 2; }
                        else if (FileIdentifier.SequenceEqual(IM3M3G)) { version = 3; }
                        else
                        {
                            Console.WriteLine("Unsupported file type: " + Path.GetFileName(M3Gfile));
                            return 0;
                        }

                        converted = ConvertM3G(inStream, FBXfile);
                    }
                }
                return converted;
            }
        }

        public static int ConvertM3G(Stream M3Gstream, string outFile)
        {
            var streamLen = M3Gstream.Length;
            List<M3Gobject> ObjectList = new List<M3Gobject>();
            ObjectList.Add(new M3Gobject()); //reserved

            using (BinaryReader binStream = new BinaryReader(M3Gstream))
            using (TextWriter sw = new StreamWriter(outFile))
            {
                int FBXmodelCount = 0;
                int FBXgeometryCount = 0;
                int FBXmaterialCount = 0;
                int FBXtextureCount = 0;

                #region Write generic FBX data
                var timestamp = DateTime.Now;
                StringBuilder fbx = new StringBuilder();
                fbx.Append("; FBX 7.1.0 project file");
                fbx.Append("\nFBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7100\n\tCreationTimeStamp:  {\n\t\tVersion: 1000");
                fbx.Append("\n\t\tYear: " + timestamp.Year);
                fbx.Append("\n\t\tMonth: " + timestamp.Month);
                fbx.Append("\n\t\tDay: " + timestamp.Day);
                fbx.Append("\n\t\tHour: " + timestamp.Hour);
                fbx.Append("\n\t\tMinute: " + timestamp.Minute);
                fbx.Append("\n\t\tSecond: " + timestamp.Second);
                fbx.Append("\n\t\tMillisecond: " + timestamp.Millisecond);
                fbx.Append("\n\t}\n\tCreator: \"M3G2FBX by Chipicao\"\n}\n");

                fbx.Append("\nGlobalSettings:  {");
                fbx.Append("\n\tVersion: 1000");
                fbx.Append("\n\tProperties70:  {");
                fbx.Append("\n\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",-1");
                fbx.Append("\n\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
                fbx.Append("\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
                fbx.Append("\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",-1");
                fbx.Append("\n\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",1");
                //sb.Append("\n\t\tP: \"AmbientColor\", \"ColorRGB\", \"Color\", \"\",0,0,0");
                //sb.Append("\n\t\tP: \"DefaultCamera\", \"KString\", \"\", \"\", \"Producer Perspective\"");
                //sb.Append("\n\t\tP: \"TimeMode\", \"enum\", \"\", \"\",6");
                //sb.Append("\n\t\tP: \"TimeProtocol\", \"enum\", \"\", \"\",2");
                //sb.Append("\n\t\tP: \"SnapOnFrameMode\", \"enum\", \"\", \"\",0");
                //sb.Append("\n\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0");
                //sb.Append("\n\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",153953860000");
                //sb.Append("\n\t\tP: \"CustomFrameRate\", \"double\", \"Number\", \"\",-1");
                //sb.Append("\n\t\tP: \"TimeMarker\", \"Compound\", \"\", \"\"");
                //sb.Append("\n\t\tP: \"CurrentTimeMarker\", \"int\", \"Integer\", \"\",-1");
                fbx.Append("\n\t}\n}\n");

                fbx.Append("\nDocuments:  {");
                fbx.Append("\n\tCount: 1");
                fbx.Append("\n\tDocument: 1234567890, \"\", \"Scene\" {");
                fbx.Append("\n\t\tProperties70:  {");
                fbx.Append("\n\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
                fbx.Append("\n\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
                fbx.Append("\n\t\t}");
                fbx.Append("\n\t\tRootNode: 0");
                fbx.Append("\n\t}\n}\n");
                fbx.Append("\nReferences:  {\n}\n");

                fbx.Append("\nDefinitions:  {");
                fbx.Append("\n\tVersion: 100");

                fbx.Append("\n\tObjectType: \"GlobalSettings\" {");
                fbx.Append("\n\t\tCount: 1");
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Model\" {");
                //sb.Append("\n\t\tCount: " + FBXmodelCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Geometry\" {");
                //sb.Append("\n\t\tCount: " + FBXgeometryCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Material\" {");
                //sb.Append("\n\t\tCount: " + FBXmaterialCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Texture\" {");
                //sb.Append("\n\t\tCount: " + FBXtextureCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Video\" {");
                //sb.Append("\n\t\tCount: " + FBXtextureCount);
                fbx.Append("\n\t}");
                fbx.Append("\n}\n");
                fbx.Append("\nObjects:  {");

                sw.Write(fbx.ToString());
                fbx.Length = 0;
                #endregion

                StringBuilder ob = new StringBuilder(); //objects builder
                //ob.Append("\nObjects:  {");

                StringBuilder cb = new StringBuilder(); //connections builder
                cb.Append("\n}\n");//Objects end
                cb.Append("\nConnections:  {");

                /*binStream.Read(FileIdentifier, 0, 12);

                    if (FileIdentifier.SequenceEqual(JSR184)) { version = 1; }
                    else if (FileIdentifier.SequenceEqual(IM3M3G)) { version = 3; }
                    else { return 0; }*/

                while (M3Gstream.Position < streamLen) //loop sections
                {
                    byte CompressionScheme = binStream.ReadByte();
                    int TotalSectionLength = binStream.ReadInt32();
                    int UncompressedLength = binStream.ReadInt32(); //version1: TotalSectionLength -13 unless compressed
                    var sectionEnd = M3Gstream.Position + TotalSectionLength - 13;

                    if (CompressionScheme == 0)
                    {

                        while (M3Gstream.Position < sectionEnd) //loop objects
                        {
                            M3Gobject M3Gobj = new M3Gobject();
                            M3Gobj.type = binStream.ReadByte();
                            M3Gobj.length = binStream.ReadInt32();
                            M3Gobj.offset = M3Gstream.Position;
                            ObjectList.Add(M3Gobj);
                            int objIndex = ObjectList.Count - 1;
                            long nextObject = M3Gstream.Position + M3Gobj.length;

                            switch (M3Gobj.type)
                            {
                                #region Image2D
                                case 10: //Image2D
                                    {
                                        FBXtextureCount++;
                                        string textureName = "";
                                        string textureFile = "";
                                        string relativePath = "";

                                        M3Gstream.Position += 8;
                                        int userParameterCount = binStream.ReadInt32();
                                        for (int i = 0; i < userParameterCount; i++)
                                        {
                                            int parameterID = binStream.ReadInt32();
                                            int parameterValueLength = binStream.ReadInt32();
                                            long nextParam = M3Gstream.Position + parameterValueLength;

                                            switch (parameterID)
                                            {
                                                case 0: //texture name
                                                    {
                                                        textureName = ReadStr(binStream, parameterValueLength);
                                                        break;
                                                    }
                                                case 900: //relative texure path
                                                    {
                                                        relativePath = ReadStr(binStream, parameterValueLength);

                                                        string sbaFile = "";
                                                        string convertedFile = "";

                                                        //first check in car folder
                                                        string[] textureFiles = Directory.GetFiles(currentPath, Path.GetFileNameWithoutExtension(relativePath) + ".*");
                                                        //then try original location
                                                        if (textureFiles.Length == 0)
                                                        {
                                                            string absolutePath = Path.GetFullPath(currentPath + Path.GetDirectoryName(relativePath));
                                                            if (!Directory.Exists(absolutePath))
                                                            {
                                                                absolutePath = absolutePath.Replace("\\published\\", "\\published.texture_pvrtc\\");
                                                            }

                                                            if (Directory.Exists(absolutePath))
                                                            {
                                                                textureFiles = Directory.GetFiles(absolutePath, Path.GetFileNameWithoutExtension(relativePath) + ".*");

                                                                if (textureFiles.Length == 0)
                                                                {
                                                                    absolutePath = absolutePath.Replace("\\published\\", "\\published.texture_pvrtc\\");
                                                                    if (Directory.Exists(absolutePath))
                                                                    {
                                                                        textureFiles = Directory.GetFiles(absolutePath, Path.GetFileNameWithoutExtension(relativePath) + ".*");
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        //last but not least check every folder from the same level
                                                        if (textureFiles.Length == 0)
                                                        {
                                                            textureFiles = Directory.GetFiles(Directory.GetParent(currentPath.TrimEnd('\\')).ToString(), Path.GetFileNameWithoutExtension(relativePath) + ".*", SearchOption.AllDirectories);
                                                        }

                                                        foreach (var tfile in textureFiles)
                                                        {//textures should already be sorted by extension: dds, png, pvr, sba
                                                            switch (Path.GetExtension(tfile))
                                                            {
                                                                case ".dds":
                                                                case ".png":
                                                                case ".pvr":
                                                                    convertedFile = tfile;
                                                                    break;
                                                                case ".sba":
                                                                    sbaFile = tfile;
                                                                    break;
                                                            }
                                                        }


                                                        if (convertedFile != "")
                                                        {
                                                            textureFile = convertedFile;
                                                            relativePath = MakeRelative(convertedFile, currentPath);
                                                        }
                                                        else if (sbaFile != "")
                                                        {
                                                            //textureFile = ConvertImage(sbaFile);
                                                            textureFile = sbaFile;
                                                            relativePath = MakeRelative(textureFile, currentPath);
                                                        }
                                                        else
                                                        {
                                                            textureFile = relativePath;
                                                        }

                                                        break;
                                                    }
                                                default:
                                                    {
                                                        M3Gstream.Position += parameterValueLength;
                                                        break;
                                                    }
                                            }
                                            M3Gstream.Position = nextParam; //failsafe
                                        }

                                        byte format = binStream.ReadByte();
                                        int width = binStream.ReadInt32();
                                        int height = binStream.ReadInt32();
                                        M3Gstream.Position += 12; //could be a bool in here

                                        ob.Append("\n\tTexture: " + (500000 + objIndex) + ", \"Texture::" + textureName + "\", \"\" {");
                                        ob.Append("\n\t\tType: \"TextureVideoClip\"");
                                        ob.Append("\n\t\tVersion: 202");
                                        ob.Append("\n\t\tTextureName: \"Texture::" + textureName + "\"");
                                        ob.Append("\n\t\tProperties70:  {");
                                        ob.Append("\n\t\t\tP: \"UVSet\", \"KString\", \"\", \"\", \"UVChannel_0\"");
                                        ob.Append("\n\t\t\tP: \"UseMaterial\", \"bool\", \"\", \"\",1");
                                        ob.Append("\n\t\t}");
                                        ob.Append("\n\t\tMedia: \"Video::" + textureName + "\"");
                                        ob.Append("\n\t\tFileName: \"" + textureFile + "\"");
                                        ob.Append("\n\t\tRelativeFilename: \"" + relativePath + "\"");
                                        ob.Append("\n\t}");

                                        ob.Append("\n\tVideo: " + (600000 + objIndex) + ", \"Video::" + textureName + "\", \"Clip\" {");
                                        ob.Append("\n\t\tType: \"Clip\"");
                                        ob.Append("\n\t\tProperties70:  {");
                                        ob.Append("\n\t\t\tP: \"Path\", \"KString\", \"XRefUrl\", \"\", \"" + textureFile + "\"");
                                        ob.Append("\n\t\t}");
                                        ob.Append("\n\t\tFileName: \"" + textureFile + "\"");
                                        ob.Append("\n\t\tRelativeFilename: \"" + relativePath + "\"");
                                        ob.Append("\n\t}");

                                        //connect video to texture
                                        cb.Append("\n\tC: \"OO\"," + (600000 + objIndex) + "," + (500000 + objIndex));

                                        break;
                                    }
                                #endregion
                                #region Appearance
                                case 3: //Appearance
                                    {
                                        FBXmaterialCount++;
                                        M3Gstream.Position += 4; //could be AnimationControllers
                                        int AnimationTracks = binStream.ReadInt32();
                                        M3Gstream.Position += AnimationTracks * 4; //ObjectIndex

                                        string materialName = "";
                                        float[] diffuseColor = new float[4] { 0.7f, 0.7f, 0.7f, 0.7f };

                                        int userParameterCount = binStream.ReadInt32();
                                        for (int i = 0; i < userParameterCount; i++)
                                        {
                                            int parameterID = binStream.ReadInt32();
                                            int parameterValueLength = binStream.ReadInt32();
                                            long nextParam = M3Gstream.Position + parameterValueLength;

                                            switch (parameterID)
                                            {
                                                case 0: //material name
                                                    {
                                                        materialName = ReadStr(binStream, parameterValueLength);
                                                        break;
                                                    }
                                                case 1:
                                                    {
                                                        string paramType = ReadStr(binStream, binStream.ReadByte());
                                                        float paramValue = binStream.ReadSingle();
                                                        break;
                                                    }
                                                case 2: //shader name?
                                                    {
                                                        string paramType = ReadStr(binStream, binStream.ReadByte());
                                                        string paramName = ReadStr(binStream, binStream.ReadInt32());
                                                        break;
                                                    }
                                                case 600:
                                                    {
                                                        float paramValue = binStream.ReadSingle();
                                                        break;
                                                    }
                                                case 601:
                                                    {
                                                        diffuseColor = new float[4] { binStream.ReadSingle(), binStream.ReadSingle(), binStream.ReadSingle(), binStream.ReadSingle() };
                                                        break;
                                                    }
                                                default:
                                                    {
                                                        M3Gstream.Position += parameterValueLength;
                                                        break;
                                                    }
                                            }
                                            M3Gstream.Position = nextParam; //failsafe
                                        }

                                        ob.Append("\n\tMaterial: " + (400000 + objIndex) + ", \"Material::" + materialName + "\", \"\" {");
                                        ob.Append("\n\t\tVersion: 102");
                                        ob.Append("\n\t\tShadingModel: \"phong\"");
                                        ob.Append("\n\t\tMultiLayer: 0");
                                        ob.Append("\n\t\tProperties70:  {");
                                        ob.Append("\n\t\t\tP: \"ShadingModel\", \"KString\", \"\", \"\", \"phong\"");
                                        ob.Append("\n\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\"," + diffuseColor[0] + "," + diffuseColor[1] + "," + diffuseColor[2]);
                                        ob.Append("\n\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\"," + diffuseColor[0] + "," + diffuseColor[1] + "," + diffuseColor[2]);
                                        ob.Append("\n\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.8,0.8,0.8");
                                        ob.Append("\n\t\t\tP: \"SpecularFactor\", \"Number\", \"\", \"A\",0");
                                        ob.Append("\n\t\t\tP: \"ShininessExponent\", \"Number\", \"\", \"A\",2");
                                        ob.Append("\n\t\t}");
                                        ob.Append("\n\t}");

                                        byte layer = binStream.ReadByte();
                                        int compositingMode = binStream.ReadInt32(); //ObjectIndex
                                        int fog = binStream.ReadInt32(); //ObjectIndex
                                        int polygonMode = binStream.ReadInt32(); //ObjectIndex
                                        int material = binStream.ReadInt32(); //ObjectIndex
                                        int textureCount = binStream.ReadInt32();
                                        for (int t = 0; t < textureCount; t++)
                                        {
                                            int textureIndex = binStream.ReadInt32();
                                            long nextTexture = M3Gstream.Position;
                                            M3Gstream.Position = ObjectList[textureIndex].offset + 14;
                                            int imageIndex = binStream.ReadInt32();

                                            //connect Texture to Material
                                            cb.Append("\n\tC: \"OP\"," + (500000 + imageIndex) + "," + (400000 + objIndex) + ", \"");
                                            switch (t)
                                            {
                                                case 0:
                                                    cb.Append("DiffuseColor\"");
                                                    break;
                                                case 1:
                                                    cb.Append("NormalMap\"");
                                                    break;
                                                default:
                                                    cb.Append("Unknown\""); //don't be a smartass
                                                    break;
                                            }

                                            /*byte ColorR = binStream.ReadByte();
                                            byte ColorG = binStream.ReadByte();
                                            byte ColorB = binStream.ReadByte();
                                            byte blending = binStream.ReadByte();
                                            byte wrappingS = binStream.ReadByte();
                                            byte wrappingT = binStream.ReadByte();
                                            byte levelFilter = binStream.ReadByte();
                                            byte imageFilter = binStream.ReadByte();*/
                                            M3Gstream.Position = nextTexture;
                                        }
                                        break;
                                    }
                                #endregion
                                #region Mesh
                                case 14: //Mesh
                                    {
                                        FBXgeometryCount++;
                                        M3Gstream.Position += 8;

                                        #region if Real Racing 3
                                        if (version == 1)
                                        {
                                            int materialID;
                                            //this is bad and you should feel bad
                                            int something = binStream.ReadInt32();
                                            switch (something)
                                            {
                                                case 5:
                                                    M3Gstream.Position += 48; 
                                                    break;
                                                case 6:
                                                    M3Gstream.Position += 56;
                                                    materialID = binStream.ReadInt32();
                                                    cb.Append("\n\tC: \"OO\"," + (40000 + materialID) + "," + (200000 + objIndex));
                                                    break;
                                                case 7:
                                                    M3Gstream.Position += 81;
                                                    materialID = binStream.ReadInt32();
                                                    cb.Append("\n\tC: \"OO\"," + (40000 + materialID) + "," + (200000 + objIndex));
                                                    break;
                                                default:
                                                    M3Gstream.Position = nextObject;
                                                    Console.WriteLine("Problem reading mesh; contact me.");
                                                    continue;
                                                    //break;
                                            }
                                            
                                            

                                            string meshName = ReadStringToNull(binStream);
                                            if (makeUnique) { meshName = objIndex.ToString() + "_" + meshName; }

                                            FBXmodelCount++;
                                            //create Mesh object
                                            ob.Append("\n\tModel: " + (200000 + objIndex) + ", \"Model::" + meshName + "\", \"Mesh\" {");
                                            ob.Append("\n\t\tVersion: 232");
                                            ob.Append("\n\t\tProperties70:  {");
                                            ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                                            ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                                            ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
                                            //ob.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                                            //ob.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\"," + (j + 2 + pmodel.nodeList.Count));
                                            ob.Append("\n\t\t}");
                                            ob.Append("\n\t\tShading: T");
                                            ob.Append("\n\t\tCulling: \"CullingOff\"");
                                            ob.Append("\n\t}"); //Model end

                                            //connect Geometry to Model
                                            cb.Append("\n\tC: \"OO\"," + (100000 + objIndex) + "," + (200000 + objIndex));

                                            //connect Model to scene; assuming hierarchy is not used in version 1
                                            cb.Append("\n\tC: \"OO\"," + (200000 + objIndex) + ",0");
                                            ObjectList[objIndex].connected = true;

                                            M3Gstream.Position += 50;
                                        }
                                        #endregion
                                        else { M3Gstream.Position += 14; }

                                        int vertexBuffer = binStream.ReadInt32();
                                        int submeshCount = binStream.ReadInt32();

                                        M3Gobj.appearance = new int[submeshCount];
                                        List<int[]> IndexBuffers = new List<int[]>();
                                        int totalIndexCount = 0;
                                        //don't collect material IDs, just write them directly when writing index arrays
                                        for (int s = 0; s < submeshCount; s++)
                                        {
                                            int indexBuffer = binStream.ReadInt32();
                                            if (version == 1) { int appearance = binStream.ReadInt32(); } //untested
                                            long nextSubmesh = M3Gstream.Position;

                                            M3Gstream.Position = ObjectList[indexBuffer].offset;
                                            bool isTriStrip = false;

                                            switch (ObjectList[indexBuffer].type)
                                            {
                                                case 11: //TriangleStripArray, but usually a list
                                                    {
                                                        M3Gstream.Position += 20;
                                                        isTriStrip = binStream.ReadBoolean();
                                                        break;
                                                    }
                                                case 100:
                                                case 103:
                                                    {
                                                        M3Gstream.Position += 12;
                                                        int realIndexBuffer = binStream.ReadInt32(); //type 101
                                                        int appearance = binStream.ReadInt32();
                                                        M3Gobj.appearance[s] = appearance;

                                                        M3Gstream.Position = ObjectList[realIndexBuffer].offset + 12;
                                                        break;
                                                    }
                                                default:
                                                    {
                                                        Console.WriteLine("Unknown Index Buffer; conversion stopped.");
                                                        return 0;
                                                    }
                                            }

                                            #region read indices
                                            byte encoding = binStream.ReadByte();
                                            int[] iarr = new int[0];
                                            switch (encoding)
                                            {
                                                case 0:
                                                    {
                                                        int startindex = binStream.ReadInt32();

                                                        int stripLengthsCount = binStream.ReadInt32();
                                                        int[] stripLengths = new int[stripLengthsCount];
                                                        for (int l = 0; l < stripLengthsCount; l++)
                                                        {
                                                            stripLengths[l] = binStream.ReadInt32();
                                                            totalIndexCount += stripLengths[l];
                                                        }
                                                        break;
                                                    }
                                                case 1:
                                                    {
                                                        byte startindex = binStream.ReadByte();

                                                        int stripLengthsCount = binStream.ReadInt32();
                                                        int[] stripLengths = new int[stripLengthsCount];
                                                        for (int l = 0; l < stripLengthsCount; l++)
                                                        {
                                                            stripLengths[l] = binStream.ReadInt32();
                                                            totalIndexCount += stripLengths[l];
                                                        }
                                                        break;
                                                    }
                                                case 2:
                                                    {
                                                        ushort startindex = binStream.ReadUInt16();

                                                        int stripLengthsCount = binStream.ReadInt32();
                                                        int[] stripLengths = new int[stripLengthsCount];
                                                        for (int l = 0; l < stripLengthsCount; l++)
                                                        {
                                                            stripLengths[l] = binStream.ReadInt32();
                                                            totalIndexCount += stripLengths[l];
                                                        }
                                                        break;
                                                    }
                                                case 128:
                                                    {
                                                        int indexCount = binStream.ReadInt32();
                                                        totalIndexCount += indexCount;
                                                        iarr = new int[indexCount];
                                                        for (int i = 0; i < indexCount; i++)
                                                        {
                                                            iarr[i] = binStream.ReadInt32();
                                                        }
                                                        break;
                                                    }
                                                case 129:
                                                    {
                                                        int indexCount = binStream.ReadInt32();
                                                        totalIndexCount += indexCount;
                                                        iarr = new int[indexCount];
                                                        for (int i = 0; i < indexCount; i++)
                                                        {
                                                            iarr[i] = binStream.ReadByte();
                                                        }
                                                        break;
                                                    }
                                                case 130:
                                                    {
                                                        int indexCount = binStream.ReadInt32();
                                                        totalIndexCount += indexCount;
                                                        iarr = new int[indexCount];
                                                        for (int i = 0; i < indexCount; i++)
                                                        {
                                                            iarr[i] = binStream.ReadUInt16();
                                                        }
                                                        break;
                                                    }
                                            }

                                            if (isTriStrip)
                                            {
                                                List<int> farr = new List<int>();
                                                for (int i = 0; i < iarr.Length - 2; i++)
                                                {
                                                    int fa = iarr[i];
                                                    int fb = iarr[i+1];
                                                    int fc = iarr[i+2];
                                                    if ((fa != fb) && (fa != fc) && (fb != fc))
                                                    {
                                                        farr.Add(fa);
                                                        if (i % 2 == 0)
                                                        {
                                                            farr.Add(fb);
                                                            farr.Add(fc);
                                                        }
                                                        else
                                                        {
                                                            farr.Add(fc);
                                                            farr.Add(fb);
                                                        }
                                                    }
                                                }
                                                totalIndexCount += farr.Count - iarr.Length;
                                                IndexBuffers.Add(farr.ToArray());
                                            }
                                            else { IndexBuffers.Add(iarr); }

                                            #endregion

                                            M3Gstream.Position = nextSubmesh;
                                        }

                                        #region read VertexBuffer
                                        M3Gstream.Position = ObjectList[vertexBuffer].offset;
                                        M3Gstream.Position += 12;
                                        byte[] ColorRGBA = new byte[4] { binStream.ReadByte(), binStream.ReadByte(), binStream.ReadByte(), binStream.ReadByte() };
                                        int positions = binStream.ReadInt32();
                                        float[] positionBias = new float[3] { binStream.ReadSingle(), binStream.ReadSingle(), binStream.ReadSingle() };
                                        float positionScale = binStream.ReadSingle();
                                        int normals = binStream.ReadInt32();
                                        int colors = binStream.ReadInt32();

                                        int texcoordArrayCount = binStream.ReadInt32();
                                        texcoordArrayCount = Math.Abs(texcoordArrayCount); //there must be soemthing special about negative numbers. but what?
                                        List<string> texcoordArrays = new List<string>();
                                        for (int t = 0; t < texcoordArrayCount; t++)
                                        {
                                            int texCoords = binStream.ReadInt32();
                                            float[] texCoordBias = new float[3] { binStream.ReadSingle(), (1 - binStream.ReadSingle()), binStream.ReadSingle() };
                                            float texCoordScale = binStream.ReadSingle();
                                            if (version == 1) { texCoordScale /= 4; } //WTF?? RR3
                                            long nextTexcoords = M3Gstream.Position;

                                            if (texCoords > 0)
                                            {
                                                M3Gstream.Position = ObjectList[texCoords].offset + 12;
                                                texcoordArrays.Add(ReadVertexArray(binStream, texCoordBias, new float[3] {texCoordScale, -texCoordScale, texCoordScale}, false));
                                            }

                                            M3Gstream.Position = nextTexcoords;
                                        }
                                        int tangents = binStream.ReadInt32();
                                        int binormals = binStream.ReadInt32();
                                        #endregion

                                        #region write Geometry
                                        ob.Append("\n\tGeometry: " + (100000 + objIndex) + ", \"Geometry::\", \"Mesh\" {");
                                        ob.Append("\n\t\tProperties70:  {");
                                        var randomColor = RandomColorGenerator((100000 + objIndex).ToString());
                                        ob.Append("\n\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\"," + ((float)randomColor[0] / 255) + "," + ((float)randomColor[1] / 255) + "," + ((float)randomColor[2] / 255));
                                        ob.Append("\n\t\t}");
                                        if (positions > 0)
                                        {
                                            M3Gstream.Position = ObjectList[positions].offset + 12;
                                            var vertexPositions = ReadVertexArray(binStream, positionBias, new float[3] { positionScale, positionScale, positionScale }, false);

                                            ob.Append("\n\t\tVertices: *");
                                            ob.Append(vertexPositions);
                                            ob.Append("\n\t\t}");
                                        }

                                        StringBuilder ib = new StringBuilder();
                                        StringBuilder mb = new StringBuilder();
                                        ob.Append("\n\t\tPolygonVertexIndex: *" + totalIndexCount.ToString() + " {\n\t\t\ta: ");
                                        for (int i = 0; i < IndexBuffers.Count; i++)
                                        {
                                            var iarr = IndexBuffers[i];
                                            for (int f = 0; f < (iarr.Length / 3); f++)
                                            {
                                                ib.Append(iarr[f * 3]);
                                                ib.Append(',');
                                                ib.Append(iarr[f * 3 + 1]);
                                                ib.Append(',');
                                                ib.Append(-iarr[f * 3 + 2] - 1);
                                                ib.Append(',');
                                                mb.Append(i + ',' + i + ',' + i + ',');//shouldn't this be just one index per face?
                                            }
                                        }
                                        ib.Length -= 1; //remove last ,
                                        mb.Length -= 1;
                                        ob.Append(SplitLine(ib.ToString()));
                                        ob.Append("\n\t\t}");
                                        ob.Append("\n\t\tGeometryVersion: 124");
                                        ib.Length = 0;
                                        mb.Length = 0; //why?
//verify
                                        if (normals > 0)
                                        {
                                            M3Gstream.Position = ObjectList[normals].offset + 12;
                                            var vertexNormals = ReadVertexArray(binStream, new float[3] { 0, 0, 0 }, new float[3] {1, 1, 1}, true);

                                            ob.Append("\n\t\tLayerElementNormal: 0 {");
                                            ob.Append("\n\t\t\tVersion: 101");
                                            ob.Append("\n\t\t\tName: \"\"");
                                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                                            ob.Append("\n\t\t\tNormals: *");
                                            ob.Append(vertexNormals);
                                            ob.Append("\n\t\t\t}\n\t\t}");
                                        }


                                        if (colors > 0)
                                        {
                                            M3Gstream.Position = ObjectList[colors].offset + 12;
                                            var vertexColors = ReadVertexArray(binStream, new float[3] { 0, 0, 0 }, new float[3] { 1, 1, 1 }, true);

                                            ob.Append("\n\t\tLayerElementColor: 0 {");
                                            ob.Append("\n\t\t\tVersion: 101");
                                            ob.Append("\n\t\t\tName: \"\"");
                                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                                            ob.Append("\n\t\t\tColors: *");
                                            ob.Append(vertexColors);
                                            ob.Append("\n\t\t\t}\n\t\t}");
                                        }

                                        for (int t = 0; t < texcoordArrays.Count; t++)
                                        {
                                            ob.Append("\n\t\tLayerElementUV: " + t + " {");
                                            ob.Append("\n\t\t\tVersion: 101");
                                            ob.Append("\n\t\t\tName: \"UVChannel_" + t + "\"");
                                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                                            ob.Append("\n\t\t\tUV: *");
                                            ob.Append(texcoordArrays[t]);
                                            ob.Append("\n\t\t\t}\n\t\t}");
                                        }

                                        ob.Append("\n\t\tLayerElementMaterial: 0 {");
                                        ob.Append("\n\t\t\tVersion: 101");
                                        ob.Append("\n\t\t\tName: \"\"");
                                        ob.Append("\n\t\t\tMappingInformationType: \"");
                                        if (IndexBuffers.Count == 1) { ob.Append("AllSame\""); }
                                        else { ob.Append("ByPolygon\""); }
                                        ob.Append("\n\t\t\tReferenceInformationType: \"IndexToDirect\"");
                                        ob.Append("\n\t\t\tMaterials: *" + IndexBuffers.Count + " {");
//no, this is supposed to be index count
                                        ob.Append("\n\t\t\t\t");
                                        if (IndexBuffers.Count == 1) { ob.Append("0"); }
                                        else { ob.Append(SplitLine(mb.ToString())); }
                                        ob.Append("\n\t\t\t}");
                                        ob.Append("\n\t\t}");

                                        ob.Append("\n\t\tLayer: 0 {");
                                        ob.Append("\n\t\t\tVersion: 100");
                                        if (normals > 0)
                                        {
                                            ob.Append("\n\t\t\tLayerElement:  {");
                                            ob.Append("\n\t\t\t\tType: \"LayerElementNormal\"");
                                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                                            ob.Append("\n\t\t\t}");
                                        }

                                        ob.Append("\n\t\t\tLayerElement:  {");
                                        ob.Append("\n\t\t\t\tType: \"LayerElementMaterial\"");
                                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                                        ob.Append("\n\t\t\t}");
                                        ob.Append("\n\t\t\tLayerElement:  {");
                                        ob.Append("\n\t\t\t\tType: \"LayerElementTexture\"");
                                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                                        ob.Append("\n\t\t\t}");
                                        ob.Append("\n\t\t\tLayerElement:  {");
                                        ob.Append("\n\t\t\t\tType: \"LayerElementBumpTextures\"");
                                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                                        ob.Append("\n\t\t\t}");
                                        if (colors > 0)
                                        {
                                            ob.Append("\n\t\t\tLayerElement:  {");
                                            ob.Append("\n\t\t\t\tType: \"LayerElementColor\"");
                                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                                            ob.Append("\n\t\t\t}");
                                        }
                                        if (texcoordArrays.Count > 0)
                                        {
                                            ob.Append("\n\t\t\tLayerElement:  {");
                                            ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                                            ob.Append("\n\t\t\t}");
                                        }
                                        ob.Append("\n\t\t}"); //Layer 0 end

                                        for (int t = 1; t < texcoordArrays.Count; t++)
                                        {
                                            ob.Append("\n\t\tLayer: " + t + " {");
                                            ob.Append("\n\t\t\tVersion: 100");
                                            ob.Append("\n\t\t\tLayerElement:  {");
                                            ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                                            ob.Append("\n\t\t\t\tTypedIndex: " + t);
                                            ob.Append("\n\t\t\t}");
                                            ob.Append("\n\t\t}"); //Layer end
                                        }

                                        ob.Append("\n\t}"); //Geometry end
                                        #endregion

                                        sw.Write(ob.ToString());
                                        ob.Length = 0;
                                        break;
                                    }
                                #endregion
                                #region Group
                                case 9: //Group
                                    {
                                        FBXmodelCount++;
                                        M3Gstream.Position += 4; //could be AnimationControllers

                                        int AnimationTracks = binStream.ReadInt32();
                                        M3Gstream.Position += AnimationTracks * 4; //ObjectIndex

                                        string groupName = "";
                                        int userParameterCount = binStream.ReadInt32();
                                        for (int i = 0; i < userParameterCount; i++)
                                        {
                                            int parameterID = binStream.ReadInt32();
                                            int parameterValueLength = binStream.ReadInt32();
                                            long nextParam = M3Gstream.Position + parameterValueLength;

                                            switch (parameterID)
                                            {
                                                case 0: //group name
                                                    {
                                                        groupName = ReadStr(binStream, parameterValueLength);
                                                        break;
                                                    }
                                                case 2:
                                                    {
                                                        string paramType = ReadStr(binStream, binStream.ReadByte());
                                                        string paramName = ReadStr(binStream, binStream.ReadInt32());
                                                        break;
                                                    }
                                                default:
                                                    {
                                                        M3Gstream.Position += parameterValueLength;
                                                        break;
                                                    }
                                            }
                                            M3Gstream.Position = nextParam; //failsafe
                                        }

                                        float[] translation = new float[3];
                                        float[] scale = new float[3];
                                        float[] EulerAngles = new float[3];
                                        bool hasComponentTransform = binStream.ReadBoolean();
                                        if (hasComponentTransform)
                                        {
                                            translation[0] = binStream.ReadSingle();
                                            translation[1] = binStream.ReadSingle();
                                            translation[2] = binStream.ReadSingle();
                                            scale[0] = binStream.ReadSingle();
                                            scale[1] = binStream.ReadSingle();
                                            scale[2] = binStream.ReadSingle();
                                            float orientationAngle = binStream.ReadSingle();
                                            float orientationAxisX = binStream.ReadSingle();
                                            float orientationAxisY = binStream.ReadSingle();
                                            float orientationAxisZ = binStream.ReadSingle();
                                            EulerAngles = AngleAxisToEuler(orientationAngle, orientationAxisX, orientationAxisY, orientationAxisZ);
                                        }

                                        bool hasGeneralTransform = binStream.ReadBoolean();
                                        if (hasGeneralTransform) //untested
                                        {
                                            M3Gstream.Position += 16 * 4;
                                        }

                                        M3Gstream.Position += 7;
                                        bool hasSomething = binStream.ReadBoolean();
                                        if (hasSomething)
                                        {
                                            short count = binStream.ReadInt16();
                                            M3Gstream.Position += count * 16; //some sort of indices
                                            short count2 = binStream.ReadInt16();
                                            for (int i = 0; i < count2; i++)
                                            {
                                                string name = ReadStr(binStream, binStream.ReadInt16());
                                                M3Gstream.Position += 9;
                                            }
                                        }

                                        int childrenCount = binStream.ReadInt32();
                                        int[] childArr = new int[childrenCount];
                                        for (int i = 0; i < childrenCount; i++) //only collect indices for further testing
                                        {
                                            int childIndex = binStream.ReadInt32();
                                            childArr[i] = childIndex;
                                        }

                                        if (childrenCount == 1 && ObjectList[childArr[0]].type == 14) //single Mesh, do not create a useless dummy node
                                        {
                                            cb.Append("\n\tC: \"OO\"," + (100000 + childArr[0]) + "," + (200000 + objIndex)); //connect Geometry to Mesh
                                            ObjectList[childArr[0]].connected = true;

                                            //connect Materials to future Model
                                            foreach (var appearance in ObjectList[childArr[0]].appearance)
                                            {
                                                cb.Append("\n\tC: \"OO\"," + (400000 + appearance) + "," + (200000 + objIndex));
                                            }

                                            if (makeUnique) { groupName = objIndex.ToString() + "_" + groupName; }
                                            ob.Append("\n\tModel: " + (200000 + objIndex) + ", \"Model::" + groupName + "\", \"Mesh\" {");
                                        }
                                        else
                                        {
                                            foreach (var childIndex in childArr)
                                            {
                                                if (ObjectList[childIndex].type == 14)
                                                {
                                                    FBXmodelCount++;
                                                    //create Mesh object
                                                    string meshName = childIndex.ToString() + "_" + groupName;
                                                    ob.Append("\n\tModel: " + (200000 + childIndex) + ", \"Model::" + meshName + "\", \"Mesh\" {");
                                                    ob.Append("\n\t\tVersion: 232");
                                                    ob.Append("\n\t\tProperties70:  {");
                                                    ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                                                    ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                                                    ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
                                                    //ob.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                                                    //ob.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\"," + (j + 2 + pmodel.nodeList.Count));
                                                    ob.Append("\n\t\t}");
                                                    ob.Append("\n\t\tShading: T");
                                                    ob.Append("\n\t\tCulling: \"CullingOff\"");
                                                    ob.Append("\n\t}"); //Model end

                                                    //connect Geometry to Model
                                                    cb.Append("\n\tC: \"OO\"," + (100000 + childIndex) + "," + (200000 + childIndex));

                                                    //connect Materials to Model
                                                    foreach (var appearance in ObjectList[childIndex].appearance)
                                                    {
                                                        cb.Append("\n\tC: \"OO\"," + (400000 + appearance) + "," + (200000 + objIndex));
                                                    }

                                                    //connect Model to parent
                                                    cb.Append("\n\tC: \"OO\"," + (200000 + childIndex) + "," + (200000 + objIndex));
                                                    ObjectList[childIndex].connected = true;
                                                }
                                                else if (ObjectList[childIndex].type == 9)
                                                {
                                                    //connect child Model to parent Model
                                                    cb.Append("\n\tC: \"OO\"," + (200000 + childIndex) + "," + (200000 + objIndex));
                                                    ObjectList[childIndex].connected = true;
                                                }
                                            }

                                            ob.Append("\n\tModel: " + (200000 + objIndex) + ", \"Model::" + groupName + "\", \"Null\" {");
                                        }

                                        //finish writing Model, be it Mesh or Null
                                        ob.Append("\n\t\tVersion: 232");
                                        ob.Append("\n\t\tProperties70:  {");
                                        ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                                        ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                                        ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
                                        if (hasComponentTransform)
                                        {
                                            ob.Append("\n\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\"," + translation[0] + "," + translation[1] + "," + translation[2]);
                                            ob.Append("\n\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\"," + EulerAngles[0] + "," + EulerAngles[1] + "," + EulerAngles[2]);
                                            ob.Append("\n\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\"," + scale[0] + "," + scale[1] + "," + scale[2]);
                                        }
                                        //ob.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                                        //ob.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\"," + (j + 2 + pmodel.nodeList.Count));
                                        ob.Append("\n\t\t}");
                                        ob.Append("\n\t\tShading: T");
                                        ob.Append("\n\t\tCulling: \"CullingOff\"");
                                        ob.Append("\n\t}"); //Model end

                                        sw.Write(ob.ToString());
                                        ob.Length = 0;

                                        break;
                                    }
                                #endregion
                                case 24: //Material list in version 1
                                    {
                                        int materialCount = binStream.ReadInt32();
                                        for (int m = 0; m < materialCount; m++)
                                        {
                                            string materialName = ReadStringToNull(binStream);
                                            ob.Append("\n\tMaterial: " + (40000 + m) + ", \"Material::" + materialName + "\", \"\" {");
                                            ob.Append("\n\t\tVersion: 102");
                                            ob.Append("\n\t\tShadingModel: \"phong\"");
                                            ob.Append("\n\t\tMultiLayer: 0");
                                            ob.Append("\n\t\tProperties70:  {");
                                            ob.Append("\n\t\t\tP: \"ShadingModel\", \"KString\", \"\", \"\", \"phong\"");
                                            ob.Append("\n\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0.7,0.7,0.7");
                                            ob.Append("\n\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",0.7,0.7,0.7");
                                            ob.Append("\n\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.8,0.8,0.8");
                                            ob.Append("\n\t\t\tP: \"SpecularFactor\", \"Number\", \"\", \"A\",0");
                                            ob.Append("\n\t\t\tP: \"ShininessExponent\", \"Number\", \"\", \"A\",2");
                                            ob.Append("\n\t\t}");
                                            ob.Append("\n\t}");
                                        }
                                        break;
                                    }
                                default:
                                    {
                                        M3Gstream.Position += M3Gobj.length;
                                        break;
                                    }
                            }

                            M3Gstream.Position = nextObject; //failsafe
                        }
                    }
                    else
                    {
                        //this will break object order if not decompressed
                        M3Gstream.Position = sectionEnd;
                        Console.WriteLine("Compressed section found; conversion stopped.");
                        return 0;
                    }

                    int checksum = binStream.ReadInt32();
                }

                //leave no one behind
                for (int i = 1; i < ObjectList.Count; i++)
                {
                    var M3Gobj = ObjectList[i];
                    switch (M3Gobj.type)
                    {
                        case 9:
                            {
                                if (!M3Gobj.connected) //connect root node to scene
                                {
                                    cb.Append("\n\tC: \"OO\"," + (200000 + i) + ",0");
                                    M3Gobj.connected = true;
                                }
                                break;
                            }
                        case 14:
                            {
                                if (!M3Gobj.connected)
                                {
                                    FBXmodelCount++;
                                    //create Model object
                                    ob.Append("\n\tModel: " + (200000 + i) + ", \"Model::" + i.ToString() + "\", \"Mesh\" {");
                                    ob.Append("\n\t\tVersion: 232");
                                    ob.Append("\n\t\tProperties70:  {");
                                    ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                                    ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                                    ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
                                    //ob.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                                    //ob.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\"," + (j + 2 + pmodel.nodeList.Count));
                                    ob.Append("\n\t\t}");
                                    ob.Append("\n\t\tShading: T");
                                    ob.Append("\n\t\tCulling: \"CullingOff\"");
                                    ob.Append("\n\t}"); //Model end

                                    //connect Geometry to Model
                                    cb.Append("\n\tC: \"OO\"," + (100000 + i) + "," + (200000 + i));

                                    //connect Materials to Model
                                    foreach (var appearance in M3Gobj.appearance)
                                    {
                                        cb.Append("\n\tC: \"OO\"," + (400000 + appearance) + "," + (200000 + i));
                                    }

                                    //connect Model to scene
                                    cb.Append("\n\tC: \"OO\"," + (200000 + i) + ",0"); //connect child to parent
                                    M3Gobj.connected = true;
                                }
                                break;
                            }
                    }
                }
                cb.Append("\n}"); //Connections end

                sw.Write(ob.ToString());
                sw.Write(cb.ToString());
            }

            return 1;
        }

        private static string ReadVertexArray(BinaryReader str, float[] bias, float[] scale, bool normalize)
        {
            StringBuilder result = new StringBuilder();
            byte componentSize = str.ReadByte();
            byte componentCount = str.ReadByte();
            byte encoding = str.ReadByte();
            ushort vertexCount = str.ReadUInt16();
            //float[] VertexArray = new float[vertexCount * componentCount];

            switch (componentSize)
            {
                case 1:
                    {
                        if (normalize)
                        {
                            scale[0] /= 127;
                            scale[1] /= 127;
                            scale[2] /= 127;
                        }
                        if (encoding == 0)
                        {
                            if (componentCount == 4) //colors
                            {
                                for (int v = 0; v < vertexCount * 4; v++)
                                {
                                    //VertexArray[v] = (float)str.ReadByte() / 255;
                                    result.Append((float)str.ReadByte() / 255);
                                    result.Append(',');
                                }
                            }
                            else
                            {
                                for (int v = 0; v < vertexCount; v++)
                                {
                                    for (int c = 0; c < componentCount; c++)
                                    {
                                        //VertexArray[v * componentCount + c] = (float)str.ReadSByte() * scale[c] + bias[c];
                                        result.Append((float)str.ReadSByte() * scale[c] + bias[c]);
                                        result.Append(',');
                                    }
                                }
                            }
                        }
                        break;
                    }
                case 2:
                    {
                        if (normalize)
                        {
                            scale[0] /= 4096;
                            scale[1] /= 4096;
                            scale[2] /= 4096;
                        }
                        if (encoding == 0)
                        {
                            for (int v = 0; v < vertexCount; v++)
                            {
                                for (int c = 0; c < componentCount; c++)
                                {
                                    //VertexArray[v * componentCount + c] = (float)str.ReadInt16() * scale[c] + bias[c];
                                    result.Append((float)str.ReadInt16() * scale[c] + bias[c]);
                                    result.Append(',');
                                }
                            }
                        }
                        break;
                    }
                case 4:
                    {
                        if (encoding == 0)
                        {
                            for (int v = 0; v < vertexCount; v++)
                            {
                                for (int c = 0; c < componentCount; c++)
                                {
                                    //VertexArray[v * componentCount + c] = str.ReadSingle() * scale + bias[c];
                                    result.Append(str.ReadSingle() * scale[c] + bias[c]);
                                    result.Append(',');
                                }
                            }
                        }
                        break;
                    }
            }

            //return VertexArray;
            result.Length -= 1; //remove last ,
            return ((vertexCount * componentCount) + " {\n\t\t\ta: " + SplitLine(result.ToString()));
        }

        /*private static string ConvertImage(string sbaFile)
        {
            using (BinaryReader sba = new BinaryReader(File.OpenRead(sbaFile)))
            {
                long sbaSize = sba.BaseStream.Length;
                sba.BaseStream.Position = 8;

                byte[] imageHeader = new byte[0];
                byte[] imageData = new byte[0];

                while (sba.BaseStream.Position < sbaSize)
                {
                    string head = ReadStr(sba, 4);
                    int length = sba.ReadInt32();
                    int checksum = sba.ReadInt32();

                    switch (head)
                    {
                        case "CHDR": //string offsets adn lengths in CDAT
                            {
                                for (int i = 0; i < length / 8; i++)
                                {
                                    int stringOffset = sba.ReadInt32();
                                    int stringLength = sba.ReadInt32();
                                }
                                break;
                            }
                        case "BARG":
                            {
                                //imageData = new byte[length];
                                imageData = sba.ReadBytes(length);
                                break;
                            }
                        default:
                            {
                                sba.BaseStream.Position += length;
                                break;
                            }
                    }

                    if ((sba.BaseStream.Position % 4) != 0) { sba.BaseStream.Position += 4 - (sba.BaseStream.Position % 4); }
                }
            }
        }*/

        private static string ReadStr(BinaryReader str, int len)
        {
            //int len = str.ReadInt32();
            byte[] stringData = new byte[len];
            str.Read(stringData, 0, len);
            var result = System.Text.Encoding.UTF8.GetString(stringData);
            return result;
        }

        public static string ReadStringToNull(BinaryReader str)
        {
            string result = "";
            char c;
            for (int i = 0; i < str.BaseStream.Length; i++)
            {
                if ((c = (char)str.ReadByte()) == 0)
                {
                    break;
                }
                result += c.ToString();
            }
            return result;
        }

        public static float[] AngleAxisToEuler(double angle, float x, float y, float z) //every god damn time
        {
            angle *= Math.PI / 180;
            double heading = 0;
            double attitude = 0;
            double bank = 0;

            double s = Math.Sin(angle);
            double c = Math.Cos(angle);
            double t = 1 - c;
            //  if axis is not already normalised then uncomment this
            // double magnitude = Math.sqrt(x*x + y*y + z*z);
            // if (magnitude==0) throw error;
            // x /= magnitude;
            // y /= magnitude;
            // z /= magnitude;
            if ((x * y * t + z * s) > 0.998)
            { // north pole singularity detected
                heading = 2 * Math.Atan2(x * Math.Sin(angle / 2), Math.Cos(angle / 2));
                attitude = Math.PI / 2;
                bank = 0;
            }
            else if ((x * y * t + z * s) < -0.998)
            { // south pole singularity detected
                heading = -2 * Math.Atan2(x * Math.Sin(angle / 2), Math.Cos(angle / 2));
                attitude = -Math.PI / 2;
                bank = 0;
            }
            else
            {
                heading = Math.Atan2(y * s - x * z * t, 1 - (y * y + z * z) * t);
                attitude = Math.Asin(x * y * t + z * s);
                bank = Math.Atan2(x * s - y * z * t, 1 - (x * x + z * z) * t);
            }

            heading *= 180 / Math.PI;
            attitude *= 180 / Math.PI;
            bank *= 180 / Math.PI;

            return new float[3] { (float)bank, (float)heading, -(float)attitude };

            //thank you Martin John Baker!
        }

        private static byte[] RandomColorGenerator(string name)
        {
            int nameHash = name.GetHashCode();
            Random r = new Random(nameHash);
            //Random r = new Random(DateTime.Now.Millisecond);

            byte red = (byte)r.Next(0, 255);
            byte green = (byte)r.Next(0, 255);
            byte blue = (byte)r.Next(0, 255);

            return new byte[3] { red, green, blue };
        }

        private static string MakeRelative(string filePath, string referencePath)
        {
            if (filePath != "" && referencePath != "")
            {
                var fileUri = new Uri(filePath);
                var referenceUri = new Uri(referencePath);
                return referenceUri.MakeRelativeUri(fileUri).ToString().Replace('/', Path.DirectorySeparatorChar);
            }
            else
            {
                return "";
            }
        }

        private static string SplitLine(string inputLine) //for FBX 2011
        {
            string outputLines = inputLine;
            int vbSplit = 0;
            for (int v = 0; v < inputLine.Length / 2048; v++)
            {
                vbSplit += 2048;
                if (vbSplit < outputLines.Length)
                {
                    vbSplit = outputLines.IndexOf(",", vbSplit) + 1;
                    if (vbSplit > 0) { outputLines = outputLines.Insert(vbSplit, "\n"); }
                }
            }
            return outputLines;
        }
    }
}
