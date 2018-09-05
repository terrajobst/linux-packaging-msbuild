// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Engine.UnitTests;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Shouldly;
using Xunit;

namespace Microsoft.Build.UnitTests
{
    public class FileMatcherTest
    {
        [Fact]
        public void GetFilesPatternMatching()
        {
            var workingPath = Path.Combine(Path.GetTempPath(), "Test");
            var files = new string[]
            {
                "Foo.cs",
                "Foo2.cs",
                "file.txt",
                "file1.txt",
                "file1.txtother",
                "fie1.txt",
                "fire1.txt",
                "file.bak.txt"
            };

            try
            {
                Directory.CreateDirectory(workingPath);
                foreach (var file in files)
                {
                    File.WriteAllBytes(Path.Combine(workingPath, file), new byte[5000]);
                }

                var patterns = new Dictionary<string, int>
                {
                    {"*.txt", 5},
                    {"???.cs", 1},
                    {"????.cs", 1},
                    {"file?.txt", 1},
                    {"fi?e?.txt", 2},
                    {"???.*", 1},
                    {"????.*", 4},
                    {"*.???", 5},
                    {"f??e1.txt", 2},
                    {"file.*.txt", 1}
                };
                foreach (var pattern in patterns)
                {
                    try
                    {
                        Assert.Equal(pattern.Value, FileMatcher.GetFiles(workingPath, pattern.Key).Length);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Pattern {pattern} failed");
                        throw;
                    }
                }
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(workingPath, true);
            }
        }

        [Theory]
        [InlineData(
            @"src\**\inner\**\*.cs", // Include
            new string[] { }, // Excludes
            new[] // Expected matchings
            {
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\foo\foo.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\baz\baz.cs",
                @"src\bar\inner\foo\foo.cs"
            }
        )]
        [InlineData(
            @"src\**\inner\**\*.cs", // Include
            new[] // Excludes
            {
                @"src\foo\inner\foo.*.cs"
            },
            new[] // Expected matchings
            {
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\foo\foo.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\baz\baz.cs",
                @"src\bar\inner\foo\foo.cs"
            }
        )]
        [InlineData(
            @"src\**\inner\**\*.cs", // Include
            new[] // Excludes
            {
                @"**\foo\**"
            },
            new[] // Expected matchings
            {
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\baz\baz.cs"
            }
        )]
        [InlineData(
            @"src\**\inner\**\*.cs", // Include
            new[] // Excludes
            {
                @"src\bar\inner\baz\**"
            },
            new[] // Expected matchings
            {
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\foo\foo.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\foo\foo.cs"
            }
        )]
        [InlineData(
            @"src\foo\**\*.cs", // Include
            new[] // Excludes
            {
                @"src\foo\**\foo\**"
            },
            new[] // Expected matchings
            {
                @"src\foo\foo.cs",
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\bar\bar.cs"
            }
        )]
        [InlineData(
            @"src\foo\inner\**\*.cs", // Include
            new[] // Excludes
            {
                @"src\foo\**\???\**"
            },
            new[] // Expected matchings
            {
                @"src\foo\inner\foo.cs"
            }
        )]
        [InlineData(
            @"**\???\**\*.cs", // Include
            new string[] { }, // Excludes
            new[] // Expected matchings
            {
                @"src\foo.cs",
                @"src\bar.cs",
                @"src\baz.cs",
                @"src\foo\foo.cs",
                @"src\bar\bar.cs",
                @"src\baz\baz.cs",
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\foo\foo.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\baz\baz.cs",
                @"src\bar\inner\foo\foo.cs",
                @"build\baz\foo.cs"
            }
        )]
        [InlineData(
            @"**\*.*", // Include
            new[] // Excludes
            {
                @"**\???\**\*.cs"
            },
            new[] // Expected matchings
            {
                @"readme.txt",
                @"licence.md"
            }
        )]
        [InlineData(
            @"**\?a?\**\?a?\*.c?", // Include
            new string[] { }, // Excludes
            new[] // Expected matchings
            {
                @"src\bar\inner\baz\baz.cs"
            }
        )]
        [InlineData(
            @"**\?a?\**\?a?.c?", // Include
            new[] // Excludes
            {
                @"**\?a?\**\?a?\*.c?"
            },
            new[] // Expected matchings
            {
                @"src\bar\bar.cs",
                @"src\baz\baz.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs"
            }
        )]
        public void GetFilesComplexGlobbingMatching(string includePattern, string[] excludePatterns, string[] expectedMatchings)
        {
            var workingPath = Path.Combine(Path.GetTempPath(), "Globbing");
            var files = new []
            {
                @"src\foo.cs",
                @"src\bar.cs",
                @"src\baz.cs",
                @"src\foo\foo.cs",
                @"src\bar\bar.cs",
                @"src\baz\baz.cs",
                @"src\foo\inner\foo.cs",
                @"src\foo\inner\foo\foo.cs",
                @"src\foo\inner\bar\bar.cs",
                @"src\bar\inner\baz.cs",
                @"src\bar\inner\baz\baz.cs",
                @"src\bar\inner\foo\foo.cs",
                @"build\baz\foo.cs",
                @"readme.txt",
                @"licence.md"
            };
            Action<string, string[], bool> match = (include, excludes, hasNoMatches) =>
            {
                string[] matchedFiles = null;
                try
                {
                    matchedFiles = FileMatcher.GetFiles(workingPath, include, excludes);
                    if (hasNoMatches)
                    {
                        Assert.Equal(0, matchedFiles.Length);
                        return;
                    }
                    // We have to lower file paths as the result could be in uppercase e.g. "SRC\foo.cs"
                    var normMatchedFiles = matchedFiles
                        .Select(o => o.Replace(Path.DirectorySeparatorChar, '\\').ToLowerInvariant()).ToArray();
                    foreach (var matchedFile in normMatchedFiles)
                    {
                        Assert.Contains(matchedFile, expectedMatchings);
                    }
                    Assert.Equal(expectedMatchings.Length, matchedFiles.Length);
                }
                catch (Exception)
                {
                    Console.WriteLine($"Globbing failed for include {include} with excludes {string.Join(",", excludes)}, " +
                                      $"should have no matches: {hasNoMatches}, " +
                                      $"returned files: {(matchedFiles != null ? string.Join(",", matchedFiles) : null)}");
                    throw;
                }
            };

            try
            {
                // Create directories and files
                foreach (var file in files)
                {
                    var normFile = file.Replace('\\', Path.DirectorySeparatorChar);
                    var dirPath = Path.Combine(workingPath, Path.GetDirectoryName(normFile));

                    Directory.CreateDirectory(dirPath);
                    File.WriteAllBytes(Path.Combine(dirPath, Path.GetFileName(normFile)), new byte[5000]);
                }

                // Normal matching
                match(includePattern, excludePatterns, false);
                // Include forward slash
                match(includePattern.Replace('\\', '/'), excludePatterns, false);
                // Excludes forward slash
                match(includePattern, excludePatterns.Select(o => o.Replace('\\', '/')).ToArray(), false);

                // Backward compatibilities:
                // 1. When an include or exclude starts with a fixed directory part e.g. "src/foo/**",
                //    then matching should be case-sensitive on Linux, as the directory was checked for its existance
                //    by using Directory.Exists, which is case-sensitive on Linux (on OSX is not).
                // 2. On Unix, when an include uses a simple ** wildcard e.g. "**\*.cs", the file pattern e.g. "*.cs",
                //    should be matched case-sensitive, as files were retrieved by using the searchPattern parameter
                //    of Directory.GetFiles, which is case-sensitive on Unix.
                var shouldHaveNoMatches = false;

                // Do not test uppercase excludes on Linux, as it is not simple to figure out which files shall
                // be excluded
                if (!NativeMethodsShared.IsLinux)
                {
                    // Excludes uppercase
                    match(includePattern, excludePatterns.Select(o => o.ToUpperInvariant()).ToArray(), false);
                }
                else
                {
                    // On Linux, we will have no matches for an uppercase include, when it starts with a fixed directory part
                    // e.g. "SRC/FOO"
                    shouldHaveNoMatches = !includePattern.StartsWith("**");
                }
                if (NativeMethodsShared.IsUnixLike)
                {
                    // On Unix, we will have no matches for an uppercase include, that has a simple ** wildcard and a file pattern
                    // e.g. "**\*.CS"
                    string fixedDirectoryPart;
                    string wildcardDirectoryPart;
                    string filenamePart;
                    string matchFileExpression;
                    bool needsRecursion;
                    bool isLegalFileSpec;
                    FileMatcher.GetFileSpecInfo(includePattern,
                        out fixedDirectoryPart,
                        out wildcardDirectoryPart,
                        out filenamePart,
                        out matchFileExpression,
                        out needsRecursion,
                        out isLegalFileSpec,
                        FileMatcher.s_defaultGetFileSystemEntries);
                    bool matchWithRegex =
                        // if we have a directory specification that uses wildcards, and
                        (wildcardDirectoryPart.Length > 0) &&
                        // the specification is not a simple "**"
                        (wildcardDirectoryPart != ("**" + new string(Path.DirectorySeparatorChar, 1)));
                    shouldHaveNoMatches |= !matchWithRegex && filenamePart.Any(char.IsLetter);
                }

                // Include uppercase
                match(includePattern.ToUpperInvariant(), excludePatterns, shouldHaveNoMatches);
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(workingPath, true);
            }
        }

        [Fact]
        public void WildcardMatching()
        {
            var inputs = new List<Tuple<string, string, bool>>
            {
                // No wildcards
                new Tuple<string, string, bool>("a", "a", true),
                new Tuple<string, string, bool>("a", "", false),
                new Tuple<string, string, bool>("", "a", false),

                // Non ASCII characters
                new Tuple<string, string, bool>("šđčćž", "šđčćž", true),

                // * wildcard
                new Tuple<string, string, bool>("abc", "*bc", true),
                new Tuple<string, string, bool>("abc", "a*c", true),
                new Tuple<string, string, bool>("abc", "ab*", true),
                new Tuple<string, string, bool>("ab", "*ab", true),
                new Tuple<string, string, bool>("ab", "a*b", true),
                new Tuple<string, string, bool>("ab", "ab*", true),
                new Tuple<string, string, bool>("aba", "ab*ba", false),
                new Tuple<string, string, bool>("", "*", true),

                // ? wildcard
                new Tuple<string, string, bool>("abc", "?bc", true),
                new Tuple<string, string, bool>("abc", "a?c", true),
                new Tuple<string, string, bool>("abc", "ab?", true),
                new Tuple<string, string, bool>("ab", "?ab", false),
                new Tuple<string, string, bool>("ab", "a?b", false),
                new Tuple<string, string, bool>("ab", "ab?", false),
                new Tuple<string, string, bool>("", "?", false),

                // Mixed wildcards
                new Tuple<string, string, bool>("a", "*?", true),
                new Tuple<string, string, bool>("a", "?*", true),
                new Tuple<string, string, bool>("ab", "*?", true),
                new Tuple<string, string, bool>("ab", "?*", true),
                new Tuple<string, string, bool>("abc", "*?", true),
                new Tuple<string, string, bool>("abc", "?*", true),

                // Multiple mixed wildcards
                new Tuple<string, string, bool>("a", "??", false),
                new Tuple<string, string, bool>("ab", "?*?", true),
                new Tuple<string, string, bool>("ab", "*?*?*", true),
                new Tuple<string, string, bool>("abc", "?**?*?", true),
                new Tuple<string, string, bool>("abc", "?**?*c?", false),
                new Tuple<string, string, bool>("abcd", "?b*??", true),
                new Tuple<string, string, bool>("abcd", "?a*??", false),
                new Tuple<string, string, bool>("abcd", "?**?c?", true),
                new Tuple<string, string, bool>("abcd", "?**?d?", false),
                new Tuple<string, string, bool>("abcde", "?*b*?*d*?", true),

                // ? wildcard in the input string
                new Tuple<string, string, bool>("?", "?", true),
                new Tuple<string, string, bool>("?a", "?a", true),
                new Tuple<string, string, bool>("a?", "a?", true),
                new Tuple<string, string, bool>("a?b", "a?", false),
                new Tuple<string, string, bool>("a?ab", "a?aab", false),
                new Tuple<string, string, bool>("aa?bbbc?d", "aa?bbc?dd", false),

                // * wildcard in the input string
                new Tuple<string, string, bool>("*", "*", true),
                new Tuple<string, string, bool>("*a", "*a", true),
                new Tuple<string, string, bool>("a*", "a*", true),
                new Tuple<string, string, bool>("a*b", "a*", true),
                new Tuple<string, string, bool>("a*ab", "a*aab", false),
                new Tuple<string, string, bool>("a*abab", "a*b", true),
                new Tuple<string, string, bool>("aa*bbbc*d", "aa*bbc*dd", false),
                new Tuple<string, string, bool>("aa*bbbc*d", "a*bbc*d", true)
            };
            foreach (var input in inputs)
            {
                try
                {
                    Assert.Equal(input.Item3, FileMatcher.IsMatch(input.Item1, input.Item2, false));
                    Assert.Equal(input.Item3, FileMatcher.IsMatch(input.Item1, input.Item2, true));
                    Assert.Equal(input.Item3, FileMatcher.IsMatch(input.Item1.ToUpperInvariant(), input.Item2, true));
                    Assert.Equal(input.Item3, FileMatcher.IsMatch(input.Item1, input.Item2.ToUpperInvariant(), true));
                }
                catch (Exception)
                {
                    Console.WriteLine($"Input {input.Item1} with pattern {input.Item2} failed");
                    throw;
                }
            }
        }

        /*
         * Method:  GetFileSystemEntries
         *
         * Simulate Directories.GetFileSystemEntries where file names are short.
         *
         */
        private static ImmutableArray<string> GetFileSystemEntries(FileMatcher.FileSystemEntity entityType, string path, string pattern, string projectDirectory, bool stripProjectDirectory)
        {
            if
            (
                pattern == @"LONGDI~1"
                && (@"D:\" == path || @"\\server\share\" == path || path.Length == 0)
            )
            {
                return ImmutableArray.Create(Path.Combine(path, "LongDirectoryName"));
            }
            else if
            (
                pattern == @"LONGSU~1"
                && (@"D:\LongDirectoryName" == path || @"\\server\share\LongDirectoryName" == path || @"LongDirectoryName" == path)
            )
            {
                return ImmutableArray.Create(Path.Combine(path, "LongSubDirectory"));
            }
            else if
            (
                pattern == @"LONGFI~1.TXT"
                && (@"D:\LongDirectoryName\LongSubDirectory" == path || @"\\server\share\LongDirectoryName\LongSubDirectory" == path || @"LongDirectoryName\LongSubDirectory" == path)
            )
            {
                return ImmutableArray.Create(Path.Combine(path, "LongFileName.txt"));
            }
            else if
            (
                pattern == @"pomegr~1"
                && @"c:\apple\banana\tomato" == path
            )
            {
                return ImmutableArray.Create(Path.Combine(path, "pomegranate"));
            }
            else if
            (
                @"c:\apple\banana\tomato\pomegranate\orange" == path
            )
            {
                // No files exist here. This is an empty directory.
                return ImmutableArray<string>.Empty;
            }
            else
            {
                Console.WriteLine("GetFileSystemEntries('{0}', '{1}')", path, pattern);
                Assert.True(false, "Unexpected input into GetFileSystemEntries");
            }
            return ImmutableArray.Create("<undefined>");
        }

        private static readonly char S = Path.DirectorySeparatorChar;

        public static IEnumerable<object[]> NormalizeTestData()
        {
            yield return new object[]
            {
                null,
                null
            };
            yield return new object[]
            {
                "",
                ""
            };
            yield return new object[]
            {
                " ",
                " "
            };

            yield return new object[]
            {
                @"\\",
                @"\\"
            };
            yield return new object[]
            {
                @"\\/\//",
                @"\\"
            };
            yield return new object[]
            {
                @"\\a/\b/\",
                $@"\\a{S}b"
            };

            yield return new object[]
            {
                @"\",
                @"\"
            };
            yield return new object[]
            {
                @"\/\/\/",
                @"\"
            };
            yield return new object[]
            {
                @"\a/\b/\",
                $@"\a{S}b"
            };

            yield return new object[]
            {
                "/",
                "/"
            };
            yield return new object[]
            {
                @"/\/\",
                "/"
            };
            yield return new object[]
            {
                @"/a\/b/\\",
                $@"/a{S}b"
            };

            yield return new object[]
            {
                @"c:\",
                @"c:\"
            };
            yield return new object[]
            {
                @"c:/",
                @"c:\"
            };
            yield return new object[]
            {
                @"c:/\/\/",
                @"c:\"
            };
            yield return new object[]
            {
                @"c:/ab",
                @"c:\ab"
            };
            yield return new object[]
            {
                @"c:\/\a//b",
                $@"c:\a{S}b"
            };
            yield return new object[]
            {
                @"c:\/\a//b\/",
                $@"c:\a{S}b"
            };

            yield return new object[]
            {
                @"..\/a\../.\b\/",
                $@"..{S}a{S}..{S}.{S}b"
            };
            yield return new object[]
            {
                @"**/\foo\/**\/",
                $@"**{S}foo{S}**"
            };

            yield return new object[]
            {
                "AbCd",
                "AbCd"
            };
        }

        [Theory]
        [MemberData(nameof(NormalizeTestData))]
        public void NormalizeTest(string inputString, string expectedString)
        {
            FileMatcher.Normalize(inputString).ShouldBe(expectedString);
        }

        /// <summary>
        /// Simple test of the MatchDriver code.
        /// </summary>
        [Fact]
        public void BasicMatchDriver()
        {
            MatchDriver
            (
                "Source" + Path.DirectorySeparatorChar + "**",
                new string[]  // Files that exist and should match.
                {
                    "Source" + Path.DirectorySeparatorChar + "Bart.txt",
                    "Source" + Path.DirectorySeparatorChar + "Sub" + Path.DirectorySeparatorChar + "Homer.txt",
                },
                new string[]  // Files that exist and should not match.
                {
                    "Destination" + Path.DirectorySeparatorChar + "Bart.txt",
                    "Destination" + Path.DirectorySeparatorChar + "Sub" + Path.DirectorySeparatorChar + "Homer.txt",
                },
                null
            );
        }

        /// <summary>
        /// This pattern should *not* recurse indefinitely since there is no '**' in the pattern:
        ///
        ///        c:\?emp\foo
        ///
        /// </summary>
        [Fact]
        public void Regress162390()
        {
            MatchDriver
            (
                @"c:\?emp\foo.txt",
                new string[] { @"c:\temp\foo.txt" },    // Should match
                new string[] { @"c:\timp\foo.txt" },    // Shouldn't match
                new string[]                            // Should not even consider.
                {
                    @"c:\temp\sub\foo.txt"
                }
            );
        }


        /*
        * Method:  GetLongFileNameForShortLocalPath
        *
        * Convert a short local path to a long path.
        *
        */
        [Fact]
        public void GetLongFileNameForShortLocalPath()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"D:\LONGDI~1\LONGSU~1\LONGFI~1.TXT",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(longPath, @"D:\LongDirectoryName\LongSubDirectory\LongFileName.txt");
        }

        /*
        * Method:  GetLongFileNameForLongLocalPath
        *
        * Convert a long local path to a long path (nop).
        *
        */
        [Fact]
        public void GetLongFileNameForLongLocalPath()
        {
            string longPath = FileMatcher.GetLongPathName
            (
                @"D:\LongDirectoryName\LongSubDirectory\LongFileName.txt",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(longPath, @"D:\LongDirectoryName\LongSubDirectory\LongFileName.txt");
        }

        /*
        * Method:  GetLongFileNameForShortUncPath
        *
        * Convert a short UNC path to a long path.
        *
        */
        [Fact]
        public void GetLongFileNameForShortUncPath()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"\\server\share\LONGDI~1\LONGSU~1\LONGFI~1.TXT",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(longPath, @"\\server\share\LongDirectoryName\LongSubDirectory\LongFileName.txt");
        }

        /*
        * Method:  GetLongFileNameForLongUncPath
        *
        * Convert a long UNC path to a long path (nop)
        *
        */
        [Fact]
        public void GetLongFileNameForLongUncPath()
        {
            string longPath = FileMatcher.GetLongPathName
            (
                @"\\server\share\LongDirectoryName\LongSubDirectory\LongFileName.txt",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(longPath, @"\\server\share\LongDirectoryName\LongSubDirectory\LongFileName.txt");
        }

        /*
        * Method:  GetLongFileNameForRelativePath
        *
        * Convert a short relative path to a long path
        *
        */
        [Fact]
        public void GetLongFileNameForRelativePath()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"LONGDI~1\LONGSU~1\LONGFI~1.TXT",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(longPath, @"LongDirectoryName\LongSubDirectory\LongFileName.txt");
        }

        /*
        * Method:  GetLongFileNameForRelativePathPreservesTrailingSlash
        *
        * Convert a short relative path with a trailing backslash to a long path
        *
        */
        [Fact]
        public void GetLongFileNameForRelativePathPreservesTrailingSlash()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"LONGDI~1\LONGSU~1\",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(@"LongDirectoryName\LongSubDirectory\", longPath);
        }

        /*
        * Method:  GetLongFileNameForRelativePathPreservesExtraSlashes
        *
        * Convert a short relative path with doubled embedded backslashes to a long path
        *
        */
        [Fact]
        public void GetLongFileNameForRelativePathPreservesExtraSlashes()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"LONGDI~1\\LONGSU~1\\",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(@"LongDirectoryName\\LongSubDirectory\\", longPath);
        }

        /*
        * Method:  GetLongFileNameForMixedLongAndShort
        *
        * Only part of the path might be short.
        *
        */
        [Fact]
        public void GetLongFileNameForMixedLongAndShort()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"c:\apple\banana\tomato\pomegr~1\orange\",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(@"c:\apple\banana\tomato\pomegranate\orange\", longPath);
        }

        /*
        * Method:  GetLongFileNameWherePartOfThePathDoesntExist
        *
        * Part of the path may not exist. In this case, we treat the non-existent parts
        * as if they were already a long file name.
        *
        */
        [Fact]
        public void GetLongFileNameWherePartOfThePathDoesntExist()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "Short names are for Windows only"
            }

            string longPath = FileMatcher.GetLongPathName
            (
                @"c:\apple\banana\tomato\pomegr~1\orange\chocol~1\vanila~1",
                new FileMatcher.GetFileSystemEntries(FileMatcherTest.GetFileSystemEntries)
            );

            Assert.Equal(@"c:\apple\banana\tomato\pomegranate\orange\chocol~1\vanila~1", longPath);
        }

        [Fact]
        public void BasicMatch()
        {
            ValidateFileMatch("file.txt", "File.txt", false);
            ValidateNoFileMatch("file.txt", "File.bin", false);
        }

        [Fact]
        public void MatchSingleCharacter()
        {
            ValidateFileMatch("file.?xt", "File.txt", false);
            ValidateNoFileMatch("file.?xt", "File.bin", false);
        }

        [Fact]
        public void MatchMultipleCharacters()
        {
            ValidateFileMatch("*.txt", "*.txt", false);
            ValidateNoFileMatch("*.txt", "*.bin", false);
        }

        [Fact]
        public void SimpleRecursive()
        {
            ValidateFileMatch("**", ".\\File.txt", true);
        }

        [Fact]
        public void DotForCurrentDirectory()
        {
            ValidateFileMatch(Path.Combine(".", "File.txt"), Path.Combine(".", "File.txt"), false);
            ValidateNoFileMatch(Path.Combine(".", "File.txt"), Path.Combine(".", "File.bin"), false);
        }

        [Fact]
        public void DotDotForParentDirectory()
        {
            ValidateFileMatch(Path.Combine("..", "..", "*.*"), Path.Combine("..", "..", "File.txt"), false);
            if (NativeMethodsShared.IsWindows)
            {
                // On Linux *. * does not pick up files with no extension
                ValidateFileMatch(Path.Combine("..", "..", "*.*"), Path.Combine("..", "..", "File"), false);
            }
            ValidateNoFileMatch(Path.Combine("..", "..", "*.*"), Path.Combine(new [] {"..", "..", "dir1", "dir2", "File.txt"}), false);
            ValidateNoFileMatch(Path.Combine("..", "..", "*.*"), Path.Combine(new [] {"..", "..", "dir1", "dir2", "File"}), false);
        }

        [Fact]
        public void ReduceDoubleSlashesBaseline()
        {
            // Baseline
            ValidateFileMatch(
                NativeMethodsShared.IsWindows ? "f:\\dir1\\dir2\\file.txt" : "/dir1/dir2/file.txt",
                NativeMethodsShared.IsWindows ? "f:\\dir1\\dir2\\file.txt" : "/dir1/dir2/file.txt",
                false);
            ValidateFileMatch(Path.Combine("**", "*.cs"), Path.Combine("dir1", "dir2", "file.cs"), true);
            ValidateFileMatch(Path.Combine("**", "*.cs"), "file.cs", true);
        }


        [Fact]
        public void ReduceDoubleSlashes()
        {
            ValidateFileMatch("f:\\\\dir1\\dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\\\dir1\\\\\\dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\\\dir1\\\\\\dir2\\\\\\\\\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("..\\**/\\*.cs", "..\\dir1\\dir2\\file.cs", true);
            ValidateFileMatch("..\\**/.\\*.cs", "..\\dir1\\dir2\\file.cs", true);
            ValidateFileMatch("..\\**\\./.\\*.cs", "..\\dir1\\dir2\\file.cs", true);
        }

        [Fact]
        public void DoubleSlashesOnBothSidesOfComparison()
        {
            ValidateFileMatch("f:\\\\dir1\\dir2\\file.txt", "f:\\\\dir1\\dir2\\file.txt", false, false);
            ValidateFileMatch("f:\\\\dir1\\\\\\dir2\\file.txt", "f:\\\\dir1\\\\\\dir2\\file.txt", false, false);
            ValidateFileMatch("f:\\\\dir1\\\\\\dir2\\\\\\\\\\file.txt", "f:\\\\dir1\\\\\\dir2\\\\\\\\\\file.txt", false, false);
            ValidateFileMatch("..\\**/\\*.cs", "..\\dir1\\dir2\\\\file.cs", true, false);
            ValidateFileMatch("..\\**/.\\*.cs", "..\\dir1\\dir2//\\file.cs", true, false);
            ValidateFileMatch("..\\**\\./.\\*.cs", "..\\dir1/\\/\\/dir2\\file.cs", true, false);
        }

        [Fact]
        public void DecomposeDotSlash()
        {
            ValidateFileMatch("f:\\.\\dir1\\dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\dir1\\.\\dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\dir1\\dir2\\.\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\.//dir1\\dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\dir1\\.//dir2\\file.txt", "f:\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch("f:\\dir1\\dir2\\.//file.txt", "f:\\dir1\\dir2\\file.txt", false);

            ValidateFileMatch(".\\dir1\\dir2\\file.txt", ".\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch(".\\.\\dir1\\dir2\\file.txt", ".\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch(".//dir1\\dir2\\file.txt", ".\\dir1\\dir2\\file.txt", false);
            ValidateFileMatch(".//.//dir1\\dir2\\file.txt", ".\\dir1\\dir2\\file.txt", false);
        }

        [Fact]
        public void RecursiveDirRecursive()
        {
            // Check that a wildcardpath of **\x\**\ matches correctly since, \**\ is a
            // separate code path.
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\x\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\y\x\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\x\y\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\y\x\y\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\x\x\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\x\x\file.txt", true);
            ValidateFileMatch(@"c:\foo\**\x\**\*.*", @"c:\foo\x\x\x\file.txt", true);
        }

        [Fact]
        public void Regress155731()
        {
            ValidateFileMatch(@"a\b\**\**\**\**\**\e\*", @"a\b\c\d\e\f.txt", true);
            ValidateFileMatch(@"a\b\**\e\*", @"a\b\c\d\e\f.txt", true);
            ValidateFileMatch(@"a\b\**\**\e\*", @"a\b\c\d\e\f.txt", true);
            ValidateFileMatch(@"a\b\**\**\**\e\*", @"a\b\c\d\e\f.txt", true);
            ValidateFileMatch(@"a\b\**\**\**\**\e\*", @"a\b\c\d\e\f.txt", true);
        }

        [Fact]
        public void ParentWithoutSlash()
        {
            // However, we don't wtool this to match,
            ValidateNoFileMatch(@"C:\foo\**", @"C:\foo", true);
            // because we don't know whether foo is a file or folder.

            // Same for UNC
            ValidateNoFileMatch
                (
                "\\\\server\\c$\\Documents and Settings\\User\\**",
                "\\\\server\\c$\\Documents and Settings\\User",
                true
                );
        }

        [Fact]
        public void Unc()
        {
            //Check UNC functionality
            ValidateFileMatch
                (
                "\\\\server\\c$\\**\\*.cs",
                "\\\\server\\c$\\Documents and Settings\\User\\Source.cs",
                true
                );

            ValidateNoFileMatch
                (
                "\\\\server\\c$\\**\\*.cs",
                "\\\\server\\c$\\Documents and Settings\\User\\Source.txt",
                true
                );
            ValidateFileMatch
                (
                "\\\\**",
                "\\\\server\\c$\\Documents and Settings\\User\\Source.cs",
                true
                );
            ValidateFileMatch
                (
                "\\\\**\\*.*",
                "\\\\server\\c$\\Documents and Settings\\User\\Source.cs",
                true
                );


            ValidateFileMatch
                (
                "**",
                "\\\\server\\c$\\Documents and Settings\\User\\Source.cs",
                true
                );
        }

        [Fact]
        public void ExplicitToolCompatibility()
        {
            // Explicit ANT compatibility. These patterns taken from the ANT documentation.
            ValidateFileMatch("**/SourceSafe/*", "./SourceSafe/Repository", true);
            ValidateFileMatch("**\\SourceSafe/*", "./SourceSafe/Repository", true);
            ValidateFileMatch("**/SourceSafe/*", ".\\SourceSafe\\Repository", true);
            ValidateFileMatch("**/SourceSafe/*", "./org/IIS/SourceSafe/Entries", true);
            ValidateFileMatch("**/SourceSafe/*", "./org/IIS/pluggin/tools/tool/SourceSafe/Entries", true);
            ValidateNoFileMatch("**/SourceSafe/*", "./org/IIS/SourceSafe/foo/bar/Entries", true);
            ValidateNoFileMatch("**/SourceSafe/*", "./SourceSafeRepository", true);
            ValidateNoFileMatch("**/SourceSafe/*", "./aSourceSafe/Repository", true);

            ValidateFileMatch("org/IIS/pluggin/**", "org/IIS/pluggin/tools/tool/docs/index.html", true);
            ValidateFileMatch("org/IIS/pluggin/**", "org/IIS/pluggin/test.xml", true);
            ValidateFileMatch("org/IIS/pluggin/**", "org/IIS/pluggin\\test.xml", true);
            ValidateNoFileMatch("org/IIS/pluggin/**", "org/IIS/abc.cs", true);

            ValidateFileMatch("org/IIS/**/SourceSafe/*", "org/IIS/SourceSafe/Entries", true);
            ValidateFileMatch("org/IIS/**/SourceSafe/*", "org\\IIS/SourceSafe/Entries", true);
            ValidateFileMatch("org/IIS/**/SourceSafe/*", "org/IIS\\SourceSafe/Entries", true);
            ValidateFileMatch("org/IIS/**/SourceSafe/*", "org/IIS/pluggin/tools/tool/SourceSafe/Entries", true);
            ValidateNoFileMatch("org/IIS/**/SourceSafe/*", "org/IIS/SourceSafe/foo/bar/Entries", true);
            ValidateNoFileMatch("org/IIS/**/SourceSafe/*", "org/IISSourceSage/Entries", true);
        }

        [Fact]
        public void ExplicitToolIncompatibility()
        {
            // NOTE: Weirdly, ANT syntax is to match a file here.
            // We don't because MSBuild philosophy is that a trailing slash indicates a directory
            ValidateNoFileMatch("**/test/**", ".\\test", true);

            // NOTE: We deviate from ANT format here. ANT would append a ** to any path
            // that ends with '/' or '\'. We think this is the wrong thing because 'folder\'
            // is a valid folder name.
            ValidateNoFileMatch("org/", "org/IISSourceSage/Entries", false);
            ValidateNoFileMatch("org\\", "org/IISSourceSage/Entries", false);
        }

        [Fact]
        public void MultipleStarStar()
        {
            // Multiple-** matches
            ValidateFileMatch("c:\\**\\user\\**\\*.*", "c:\\Documents and Settings\\user\\NTUSER.DAT", true);
            ValidateNoFileMatch("c:\\**\\user1\\**\\*.*", "c:\\Documents and Settings\\user\\NTUSER.DAT", true);
            ValidateFileMatch("c:\\**\\user\\**\\*.*", "c://Documents and Settings\\user\\NTUSER.DAT", true);
            ValidateNoFileMatch("c:\\**\\user1\\**\\*.*", "c:\\Documents and Settings//user\\NTUSER.DAT", true);
        }

        [Fact]
        public void RegressItemRecursionWorksAsExpected()
        {
            // Regress bug#54411:  Item recursion doesn't work as expected on "c:\foo\**"
            ValidateFileMatch("c:\\foo\\**", "c:\\foo\\two\\subfile.txt", true);
        }

        [Fact]
        public void IllegalPaths()
        {
            // Certain patterns are illegal.
            ValidateIllegal("**.cs");
            ValidateIllegal("***");
            ValidateIllegal("****");
            ValidateIllegal("*.cs**");
            ValidateIllegal("*.cs**");
            ValidateIllegal("...\\*.cs");
            ValidateIllegal("http://www.website.com");
            ValidateIllegal("<:tag:>");
            ValidateIllegal("<:\\**");
        }

        [Fact]
        [PlatformSpecific(Xunit.PlatformID.Windows)] // Nothing's too long for Unix
        public void IllegalTooLongPath()
        {
            string longString = new string('X', 500) + "*"; // need a wildcard to do anything
            string[] result = FileMatcher.GetFiles(@"c:\", longString);

            Assert.Equal(longString, result[0]); // Does not throw

            // Not checking that GetFileSpecMatchInfo returns the illegal-path flag,
            // not certain that won't break something; this fix is merely to avoid a crash.
        }

        [Fact]
        public void SplitFileSpec()
        {
            /*************************************************************************************
            * Call ValidateSplitFileSpec with various supported combinations.
            *************************************************************************************/
            ValidateSplitFileSpec("foo.cs", "", "", "foo.cs");
            ValidateSplitFileSpec("**\\foo.cs", "", "**\\", "foo.cs");
            ValidateSplitFileSpec("f:\\dir1\\**\\foo.cs", "f:\\dir1\\", "**\\", "foo.cs");
            ValidateSplitFileSpec("..\\**\\foo.cs", "..\\", "**\\", "foo.cs");
            ValidateSplitFileSpec("f:\\dir1\\foo.cs", "f:\\dir1\\", "", "foo.cs");
            ValidateSplitFileSpec("f:\\dir?\\foo.cs", "f:\\", "dir?\\", "foo.cs");
            ValidateSplitFileSpec("dir?\\foo.cs", "", "dir?\\", "foo.cs");
            ValidateSplitFileSpec(@"**\test\**", "", @"**\test\**\", "*.*");
            ValidateSplitFileSpec("bin\\**\\*.cs", "bin\\", "**\\", "*.cs");
            ValidateSplitFileSpec("bin\\**\\*.*", "bin\\", "**\\", "*.*");
            ValidateSplitFileSpec("bin\\**", "bin\\", "**\\", "*.*");
            ValidateSplitFileSpec("bin\\**\\", "bin\\", "**\\", "");
            ValidateSplitFileSpec("bin\\**\\*", "bin\\", "**\\", "*");
            ValidateSplitFileSpec("**", "", "**\\", "*.*");

        }

        [Fact]
        public void Regress367780_CrashOnStarDotDot()
        {
            string workingPath = Path.Combine(Path.GetTempPath(), "Regress367780");
            string workingPathSubfolder = Path.Combine(workingPath, "SubDir");
            string offendingPattern = Path.Combine(workingPath, @"*\..\bar");
            string[] files = new string[0];

            try
            {
                Directory.CreateDirectory(workingPath);
                Directory.CreateDirectory(workingPathSubfolder);

                files = FileMatcher.GetFiles(workingPath, offendingPattern);
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(workingPathSubfolder);
                FileUtilities.DeleteWithoutTrailingBackslash(workingPath);
            }
        }

        [Fact]
        public void Regress141071_StarStarSlashStarStarIsLiteral()
        {
            string workingPath = Path.Combine(Path.GetTempPath(), "Regress141071");
            string fileName = Path.Combine(workingPath, "MyFile.txt");
            string offendingPattern = Path.Combine(workingPath, @"**\**");

            string[] files = new string[0];

            try
            {
                Directory.CreateDirectory(workingPath);
                File.WriteAllText(fileName, "Hello there.");
                files = FileMatcher.GetFiles(workingPath, offendingPattern);
            }
            finally
            {
                File.Delete(fileName);
                FileUtilities.DeleteWithoutTrailingBackslash(workingPath);
            }

            string result = String.Join(", ", files);
            Console.WriteLine(result);
            Assert.False(result.Contains("**"));
            Assert.True(result.Contains("MyFile.txt"));
        }

        [Fact]
        public void Regress14090_TrailingDotMatchesNoExtension()
        {
            string workingPath = Path.Combine(Path.GetTempPath(), "Regress141071");
            string workingPathSubdir = Path.Combine(workingPath, "subdir");
            string workingPathSubdirBing = Path.Combine(workingPathSubdir, "bing");

            string offendingPattern = Path.Combine(workingPath, @"**\sub*\*.");

            string[] files = new string[0];

            try
            {
                Directory.CreateDirectory(workingPath);
                Directory.CreateDirectory(workingPathSubdir);
                File.AppendAllText(workingPathSubdirBing, "y");
                files = FileMatcher.GetFiles(workingPath, offendingPattern);
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(workingPath, true);
            }

            string result = String.Join(", ", files);
            Console.WriteLine(result);
            Assert.Equal(1, files.Length);
        }

        [Fact]
        public void Regress14090_TrailingDotMatchesNoExtension_Part2()
        {
            ValidateFileMatch(@"c:\mydir\**\*.", @"c:\mydir\subdir\bing", true, /* simulate filesystem? */ false);
            ValidateNoFileMatch(@"c:\mydir\**\*.", @"c:\mydir\subdir\bing.txt", true);
        }

        [Fact]
        public void FileEnumerationCacheTakesExcludesIntoAccount()
        {
            try
            {
                using (var env = TestEnvironment.Create())
                {
                    env.SetEnvironmentVariable("MsBuildCacheFileEnumerations", "1");

                    var testProject = env.CreateTestProjectWithFiles(string.Empty, new[] {"a.cs", "b.cs", "c.cs"});

                    var files = FileMatcher.GetFiles(testProject.TestRoot, "**/*.cs");
                    Array.Sort(files);
                    Assert.Equal(new []{"a.cs", "b.cs", "c.cs"}, files);

                    files = FileMatcher.GetFiles(testProject.TestRoot, "**/*.cs", new []{"a.cs"});
                    Array.Sort(files);
                    Assert.Equal(new[] {"b.cs", "c.cs" }, files);

                    files = FileMatcher.GetFiles(testProject.TestRoot, "**/*.cs", new []{"a.cs", "c.cs"});
                    Array.Sort(files);
                    Assert.Equal(new[] {"b.cs" }, files);
                }
            }
            finally
            {
                FileMatcher.ClearFileEnumerationsCache();
            }
        }

        [Fact]
        public void RemoveProjectDirectory()
        {
            string[] strings = new string[1] { NativeMethodsShared.IsWindows ? "c:\\1.file" : "/1.file" };
            strings = FileMatcher.RemoveProjectDirectory(strings, NativeMethodsShared.IsWindows ? "c:\\" : "/").ToArray();
            Assert.Equal(strings[0], "1.file");

            strings = new string[1] { NativeMethodsShared.IsWindows ? "c:\\directory\\1.file" : "/directory/1.file"};
            strings = FileMatcher.RemoveProjectDirectory(strings, NativeMethodsShared.IsWindows ? "c:\\" : "/").ToArray();
            Assert.Equal(strings[0], NativeMethodsShared.IsWindows ? "directory\\1.file" : "directory/1.file");

            strings = new string[1] { NativeMethodsShared.IsWindows ? "c:\\directory\\1.file" : "/directory/1.file" };
            strings = FileMatcher.RemoveProjectDirectory(strings, NativeMethodsShared.IsWindows ? "c:\\directory" : "/directory").ToArray();
            Assert.Equal(strings[0], "1.file");

            strings = new string[1] { NativeMethodsShared.IsWindows ? "c:\\1.file" : "/1.file" };
            strings = FileMatcher.RemoveProjectDirectory(strings, NativeMethodsShared.IsWindows ? "c:\\directory" : "/directory" ).ToArray();
            Assert.Equal(strings[0], NativeMethodsShared.IsWindows ? "c:\\1.file" : "/1.file");

            strings = new string[1] { NativeMethodsShared.IsWindows ? "c:\\directorymorechars\\1.file" : "/directorymorechars/1.file" };
            strings = FileMatcher.RemoveProjectDirectory(strings, NativeMethodsShared.IsWindows ? "c:\\directory" : "/directory").ToArray();
            Assert.Equal(strings[0], NativeMethodsShared.IsWindows ? "c:\\directorymorechars\\1.file" : "/directorymorechars/1.file" );

            if (NativeMethodsShared.IsWindows)
            {
                strings = new string[1] { "\\Machine\\1.file" };
                strings = FileMatcher.RemoveProjectDirectory(strings, "\\Machine").ToArray();
                Assert.Equal(strings[0], "1.file");

                strings = new string[1] { "\\Machine\\directory\\1.file" };
                strings = FileMatcher.RemoveProjectDirectory(strings, "\\Machine").ToArray();
                Assert.Equal(strings[0], "directory\\1.file");

                strings = new string[1] { "\\Machine\\directory\\1.file" };
                strings = FileMatcher.RemoveProjectDirectory(strings, "\\Machine\\directory").ToArray();
                Assert.Equal(strings[0], "1.file");

                strings = new string[1] { "\\Machine\\1.file" };
                strings = FileMatcher.RemoveProjectDirectory(strings, "\\Machine\\directory").ToArray();
                Assert.Equal(strings[0], "\\Machine\\1.file");

                strings = new string[1] { "\\Machine\\directorymorechars\\1.file" };
                strings = FileMatcher.RemoveProjectDirectory(strings, "\\Machine\\directory").ToArray();
                Assert.Equal(strings[0], "\\Machine\\directorymorechars\\1.file");
            }
        }

        [Theory]
        [InlineData(
            @"src/**/*.cs", //  Include Pattern
            new string[] //  Matching files
            {
                @"src/a.cs",
                @"src/a\b\b.cs",
            }
            )]
        [InlineData(
            @"src/test/**/*.cs", //  Include Pattern
            new string[] //  Matching files
            {
                @"src/test/a.cs",
                @"src/test/a\b\c.cs",
            }
            )]
        [InlineData(
            @"src/test/**/a/b/**/*.cs", //  Include Pattern
            new string[] //  Matching files
            {
                @"src/test/dir\a\b\a.cs",
                @"src/test/dir\a\b\c\a.cs",
            }
            )]
        public void IncludePatternShouldNotPreserveUserSlashesInFixedDirPart(string include, string[] matching)
        {
            MatchDriver(include, null, matching, null, null, normalizeAllPaths: false, normalizeExpectedMatchingFiles: true);
        }

        [Theory]
        [InlineData(
            @"**\*.cs", //  Include Pattern
            new[] //  Exclude patterns
            {
                @"bin\**"
            },
            new string[] //  Matching files
            {
            },
            new string[] //  Non matching files
            {
            },
            new[] //  Non matching files that shouldn't be touched
            {
                @"bin\foo.cs",
                @"bin\bar\foo.cs",
                @"bin\bar\"
            }
            )]
        [InlineData(
            @"**\*.cs", //  Include Pattern
            new[] //  Exclude patterns
            {
                @"bin\**"
            },
            new[] //  Matching files
            {
                "a.cs",
                @"b\b.cs",
            },
            new[] //  Non matching files
            {
                @"b\b.txt"
            },
            new[] //  Non matching files that shouldn't be touched
            {
                @"bin\foo.cs",
                @"bin\bar\foo.cs",
                @"bin\bar\"
            }
            )]
        public void ExcludePattern(string include, string[] exclude, string[] matching, string[] nonMatching, string[] untouchable)
        {
            MatchDriver(include, exclude, matching, nonMatching, untouchable);
        }

        [Fact]
        public void ExcludeSpecificFiles()
        {
            MatchDriverWithDifferentSlashes(
                @"**\*.cs",     //  Include Pattern
                new[]    //  Exclude patterns
                {
                    @"Program_old.cs",
                    @"Properties\AssemblyInfo_old.cs"
                },
                new[]    //  Matching files
                {
                    @"foo.cs",
                    @"Properties\AssemblyInfo.cs",
                    @"Foo\Bar\Baz\Buzz.cs"
                },
                new[]    //  Non matching files
                {
                    @"Program_old.cs",
                    @"Properties\AssemblyInfo_old.cs"
                },
                new string[]    //  Non matching files that shouldn't be touched
                {
                }
            );
        }

        [Fact]
        public void ExcludePatternAndSpecificFiles()
        {
            MatchDriverWithDifferentSlashes(
                @"**\*.cs",     //  Include Pattern
                new[]    //  Exclude patterns
                {
                    @"bin\**",
                    @"Program_old.cs",
                    @"Properties\AssemblyInfo_old.cs"

                },
                new[]    //  Matching files
                {
                    @"foo.cs",
                    @"Properties\AssemblyInfo.cs",
                    @"Foo\Bar\Baz\Buzz.cs"
                },
                new[]    //  Non matching files
                {
                    @"foo.txt",
                    @"Foo\foo.txt",
                    @"Program_old.cs",
                    @"Properties\AssemblyInfo_old.cs"
                },
                new[]    //  Non matching files that shouldn't be touched
                {
                    @"bin\foo.cs",
                    @"bin\bar\foo.cs",
                    @"bin\bar\"
                }
            );
        }

        [Theory]
        [InlineData(
            @"**\*.cs", // Include Pattern
            new[] // Exclude patterns
            {
                @"**\bin\**\*.cs",
                @"src\Common\**",
            },
            new[] // Matching files
            {
                @"foo.cs",
                @"src\Framework\Properties\AssemblyInfo.cs",
                @"src\Framework\Foo\Bar\Baz\Buzz.cs"
            },
            new[] // Non matching files
            {
                @"foo.txt",
                @"src\Framework\Readme.md",
                @"src\Common\foo.cs",

                // Ideally these would be untouchable
                @"src\Framework\bin\foo.cs",
                @"src\Framework\bin\Debug",
                @"src\Framework\bin\Debug\foo.cs",
            },
            new[] // Non matching files that shouldn't be touched
            {
                @"src\Common\Properties\",
                @"src\Common\Properties\AssemblyInfo.cs",
            }
            )]
        [InlineData(
            @"**\*.cs", // Include Pattern
            new[] // Exclude patterns
            {
                @"**\bin\**\*.cs",
                @"src\Co??on\**",
            },
            new[] // Matching files
            {
                @"foo.cs",
                @"src\Framework\Properties\AssemblyInfo.cs",
                @"src\Framework\Foo\Bar\Baz\Buzz.cs"
            },
            new[] // Non matching files
            {
                @"foo.txt",
                @"src\Framework\Readme.md",
                @"src\Common\foo.cs",

                // Ideally these would be untouchable
                @"src\Framework\bin\foo.cs",
                @"src\Framework\bin\Debug",
                @"src\Framework\bin\Debug\foo.cs",
                @"src\Common\Properties\AssemblyInfo.cs"
            },
            new[] // Non matching files that shouldn't be touched
            {
                @"src\Common\Properties\"
            }
        )]
        [InlineData(
            @"src\**\proj\**\*.cs", // Include Pattern
            new[] // Exclude patterns
            {
                @"src\**\proj\**\none\**\*",
            },
            new[] // Matching files
            {
                @"src\proj\m1.cs",
                @"src\proj\a\m2.cs",
                @"src\b\proj\m3.cs",
                @"src\c\proj\d\m4.cs",
            },
            new[] // Non matching files
            {
                @"nm1.cs",
                @"a\nm2.cs",
                @"src\nm3.cs",
                @"src\a\nm4.cs",

                // Ideally these would be untouchable
                @"src\proj\none\nm5.cs",
                @"src\proj\a\none\nm6.cs",
                @"src\b\proj\none\nm7.cs",
                @"src\c\proj\d\none\nm8.cs",
                @"src\e\proj\f\none\g\nm8.cs",
            },
            new string[] // Non matching files that shouldn't be touched
            {
            }
            )]
        // patterns with excludes that ideally would prune entire recursive subtrees (files in pruned tree aren't touched at all) but the exclude pattern is too complex for that to work with the current logic
        public void ExcludeComplexPattern(string include, string[] exclude, string[] matching, string[] nonMatching, string[] untouchable)
        {
            MatchDriverWithDifferentSlashes(include, exclude, matching, nonMatching, untouchable);
        }


        #region Support functions.

        /// <summary>
        /// This support class simulates a file system.
        /// It accepts multiple sets of files and keeps track of how many files were "hit"
        /// In this case, "hit" means that the caller asked for that file directly.
        /// </summary>
        internal class MockFileSystem
        {
            /// <summary>
            /// Array of files (set1)
            /// </summary>
            private string[] _fileSet1;

            /// <summary>
            /// Array of files (set2)
            /// </summary>
            private string[] _fileSet2;

            /// <summary>
            /// Array of files (set3)
            /// </summary>
            private string[] _fileSet3;

            /// <summary>
            /// Number of times a file from set 1 was requested.
            /// </summary>
            private int _fileSet1Hits = 0;

            /// <summary>
            /// Number of times a file from set 2 was requested.
            /// </summary>
            private int _fileSet2Hits = 0;

            /// <summary>
            /// Number of times a file from set 3 was requested.
            /// </summary>
            private int _fileSet3Hits = 0;

            /// <summary>
            /// Construct.
            /// </summary>
            /// <param name="fileSet1">First set of files.</param>
            /// <param name="fileSet2">Second set of files.</param>
            /// <param name="fileSet3">Third set of files.</param>
            internal MockFileSystem
            (
                string[] fileSet1,
                string[] fileSet2,
                string[] fileSet3
            )
            {
                _fileSet1 = fileSet1;
                _fileSet2 = fileSet2;
                _fileSet3 = fileSet3;
            }

            /// <summary>
            /// Number of times a file from set 1 was requested.
            /// </summary>
            internal int FileHits1
            {
                get { return _fileSet1Hits; }
            }

            /// <summary>
            /// Number of times a file from set 2 was requested.
            /// </summary>
            internal int FileHits2
            {
                get { return _fileSet2Hits; }
            }

            /// <summary>
            /// Number of times a file from set 3 was requested.
            /// </summary>
            internal int FileHits3
            {
                get { return _fileSet3Hits; }
            }

            /// <summary>
            /// Return files that match the given files.
            /// </summary>
            /// <param name="candidates">Candidate files.</param>
            /// <param name="path">The path to search within</param>
            /// <param name="pattern">The pattern to search for.</param>
            /// <param name="files">Hashtable receives the files.</param>
            /// <returns></returns>
            private int GetMatchingFiles(string[] candidates, string path, string pattern, ISet<string> files)
            {
                int hits = 0;

                if (candidates != null)
                {
                    foreach (string candidate in candidates)
                    {
                        string normalizedCandidate = Normalize(candidate);

                        // Get the candidate directory.
                        string candidateDirectoryName = "";
                        if (normalizedCandidate.IndexOfAny(FileMatcher.directorySeparatorCharacters) != -1)
                        {
                            candidateDirectoryName = Path.GetDirectoryName(normalizedCandidate);
                        }

                        // Does the candidate directory match the requested path?
                        if (FileUtilities.PathsEqual(path, candidateDirectoryName))
                        {
                            // Match the basic *.* or null. These both match any file.
                            if
                            (
                                pattern == null ||
                                String.Compare(pattern, "*.*", StringComparison.OrdinalIgnoreCase) == 0
                            )
                            {
                                ++hits;
                                files.Add(FileMatcher.Normalize(candidate));
                            }
                            else if (pattern.Substring(0, 2) == "*.") // Match patterns like *.cs
                            {
                                string tail = pattern.Substring(1);
                                string candidateTail = candidate.Substring(candidate.Length - tail.Length);
                                if (String.Compare(tail, candidateTail, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    ++hits;
                                    files.Add(FileMatcher.Normalize(candidate));
                                }
                            }
                            else if (pattern.Substring(pattern.Length - 4, 2) == ".?") // Match patterns like foo.?xt
                            {
                                string leader = pattern.Substring(0, pattern.Length - 4);
                                string candidateLeader = candidate.Substring(candidate.Length - leader.Length - 4, leader.Length);
                                if (String.Compare(leader, candidateLeader, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    string tail = pattern.Substring(pattern.Length - 2);
                                    string candidateTail = candidate.Substring(candidate.Length - 2);
                                    if (String.Compare(tail, candidateTail, StringComparison.OrdinalIgnoreCase) == 0)
                                    {
                                        ++hits;
                                        files.Add(FileMatcher.Normalize(candidate));
                                    }
                                }
                            }
                            else if (!FileMatcher.HasWildcards(pattern))
                            {
                                if (normalizedCandidate == Path.Combine(path, pattern))
                                {
                                    ++hits;
                                    files.Add(candidate);
                                }
                            }
                            else
                            {
                                Assert.True(false, String.Format("Unhandled case in GetMatchingFiles: {0}", pattern));
                            }
                        }
                    }
                }

                return hits;
            }

            /// <summary>
            /// Given a path and pattern, return all the simulated directories out of candidates.
            /// </summary>
            /// <param name="candidates">Candidate file to extract directories from.</param>
            /// <param name="path">The path to search.</param>
            /// <param name="pattern">The pattern to match.</param>
            /// <param name="directories">Receives the directories.</param>
            private void GetMatchingDirectories(string[] candidates, string path, string pattern, ISet<string> directories)
            {
                if (candidates != null)
                {
                    foreach (string candidate in candidates)
                    {
                        string normalizedCandidate = Normalize(candidate);

                        if (IsMatchingDirectory(path, normalizedCandidate))
                        {
                            int nextSlash = normalizedCandidate.IndexOfAny(FileMatcher.directorySeparatorCharacters, path.Length + 1);
                            if (nextSlash != -1)
                            {
                                string match;

                                //UNC paths start with a \\ fragment. Match against \\ when path is empty (i.e., inside the current working directory)
                                match = normalizedCandidate.StartsWith(@"\\") && string.IsNullOrEmpty(path)
                                    ? @"\\"
                                    : normalizedCandidate.Substring(0, nextSlash);

                                string baseMatch = Path.GetFileName(normalizedCandidate.Substring(0, nextSlash));

                                if
                                (
                                    String.Compare(pattern, "*.*", StringComparison.OrdinalIgnoreCase) == 0
                                    || pattern == null
                                )
                                {
                                    directories.Add(FileMatcher.Normalize(match));
                                }
                                else if    // Match patterns like ?emp
                                    (
                                    pattern.Substring(0, 1) == "?"
                                    && pattern.Length == baseMatch.Length
                                )
                                {
                                    string tail = pattern.Substring(1);
                                    string baseMatchTail = baseMatch.Substring(1);
                                    if (String.Compare(tail, baseMatchTail, StringComparison.OrdinalIgnoreCase) == 0)
                                    {
                                        directories.Add(FileMatcher.Normalize(match));
                                    }
                                }
                                else
                                {
                                    Assert.True(false, String.Format("Unhandled case in GetMatchingDirectories: {0}", pattern));
                                }
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Method that is delegable for use by FileMatcher. This method simulates a filesystem by returning
            /// files and\or folders that match the requested path and pattern.
            /// </summary>
            /// <param name="entityType">Files, Directories or both</param>
            /// <param name="path">The path to search.</param>
            /// <param name="pattern">The pattern to search (may be null)</param>
            /// <returns>The matched files or folders.</returns>
            internal ImmutableArray<string> GetAccessibleFileSystemEntries(FileMatcher.FileSystemEntity entityType, string path, string pattern, string projectDirectory, bool stripProjectDirectory)
            {
                string normalizedPath = Normalize(path);

                ISet<string> files = new HashSet<string>();
                if (entityType == FileMatcher.FileSystemEntity.Files || entityType == FileMatcher.FileSystemEntity.FilesAndDirectories)
                {
                    _fileSet1Hits += GetMatchingFiles(_fileSet1, normalizedPath, pattern, files);
                    _fileSet2Hits += GetMatchingFiles(_fileSet2, normalizedPath, pattern, files);
                    _fileSet3Hits += GetMatchingFiles(_fileSet3, normalizedPath, pattern, files);
                }

                if (entityType == FileMatcher.FileSystemEntity.Directories || entityType == FileMatcher.FileSystemEntity.FilesAndDirectories)
                {
                    GetMatchingDirectories(_fileSet1, normalizedPath, pattern, files);
                    GetMatchingDirectories(_fileSet2, normalizedPath, pattern, files);
                    GetMatchingDirectories(_fileSet3, normalizedPath, pattern, files);
                }

                return files.ToImmutableArray();
            }

            /// <summary>
            /// Given a path, fix it up so that it can be compared to another path.
            /// </summary>
            /// <param name="path">The path to fix up.</param>
            /// <returns>The normalized path.</returns>
            internal static string Normalize(string path)
            {
                if (path.Length == 0)
                {
                    return path;
                }

                string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (Path.DirectorySeparatorChar != '\\')
                {
                    normalized = path.Replace("\\", Path.DirectorySeparatorChar.ToString());
                }
                // Replace leading UNC.
                if (normalized.StartsWith(@"\\"))
                {
                    normalized = "<:UNC:>" + normalized.Substring(2);
                }

                // Preserve parent-directory markers.
                normalized = normalized.Replace(@".." + Path.DirectorySeparatorChar, "<:PARENT:>");


                // Just get rid of doubles enough to satisfy our test cases.
                string doubleSeparator = Path.DirectorySeparatorChar.ToString() + Path.DirectorySeparatorChar.ToString();
                normalized = normalized.Replace(doubleSeparator, Path.DirectorySeparatorChar.ToString());
                normalized = normalized.Replace(doubleSeparator, Path.DirectorySeparatorChar.ToString());
                normalized = normalized.Replace(doubleSeparator, Path.DirectorySeparatorChar.ToString());

                // Strip any .\
                normalized = normalized.Replace(@"." + Path.DirectorySeparatorChar, "");

                // Put back the preserved markers.
                normalized = normalized.Replace("<:UNC:>", @"\\");
                normalized = normalized.Replace("<:PARENT:>", @".." + Path.DirectorySeparatorChar);

                return normalized;
            }

            /// <summary>
            /// Determines whether candidate is in a subfolder of path.
            /// </summary>
            /// <param name="path"></param>
            /// <param name="candidate"></param>
            /// <returns>True if there is a match.</returns>
            private bool IsMatchingDirectory(string path, string candidate)
            {
                string normalizedPath = Normalize(path);
                string normalizedCandidate = Normalize(candidate);

                // Current directory always matches for non-rooted paths.
                if (path.Length == 0 && !Path.IsPathRooted(candidate))
                {
                    return true;
                }

                if (normalizedCandidate.Length > normalizedPath.Length)
                {
                    if (String.Compare(normalizedPath, 0, normalizedCandidate, 0, normalizedPath.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (FileUtilities.EndsWithSlash(normalizedPath))
                        {
                            return true;
                        }
                        else if (FileUtilities.IsSlash(normalizedCandidate[normalizedPath.Length]))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }


            /// <summary>
            /// Searches the candidates array for one that matches path
            /// </summary>
            /// <param name="path"></param>
            /// <param name="candidates"></param>
            /// <returns>The index of the first match or negative one.</returns>
            private int IndexOfFirstMatchingDirectory(string path, string[] candidates)
            {
                if (candidates != null)
                {
                    int i = 0;
                    foreach (string candidate in candidates)
                    {
                        if (IsMatchingDirectory(path, candidate))
                        {
                            return i;
                        }

                        ++i;
                    }
                }

                return -1;
            }

            /// <summary>
            /// Delegable method that returns true if the given directory exists in this simulated filesystem
            /// </summary>
            /// <param name="path">The path to check.</param>
            /// <returns>True if the directory exists.</returns>
            internal bool DirectoryExists(string path)
            {
                if (IndexOfFirstMatchingDirectory(path, _fileSet1) != -1)
                {
                    return true;
                }

                if (IndexOfFirstMatchingDirectory(path, _fileSet2) != -1)
                {
                    return true;
                }

                if (IndexOfFirstMatchingDirectory(path, _fileSet3) != -1)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// A general purpose method used to:
        ///
        /// (1) Simulate a file system.
        /// (2) Check whether all matchingFiles where hit by the filespec pattern.
        /// (3) Check whether all nonmatchingFiles were *not* hit by the filespec pattern.
        /// (4) Check whether all untouchableFiles were not even requested (usually for perf reasons).
        ///
        /// These can be used in various combinations to test the filematcher framework.
        /// </summary>
        /// <param name="filespec">A FileMatcher filespec, possibly with wildcards.</param>
        /// <param name="matchingFiles">Files that exist and should be matched.</param>
        /// <param name="nonmatchingFiles">Files that exists and should not be matched.</param>
        /// <param name="untouchableFiles">Files that exist but should not be requested.</param>
        private static void MatchDriver
        (
            string filespec,
            string[] matchingFiles,
            string[] nonmatchingFiles,
            string[] untouchableFiles
        )
        {
            MatchDriver(filespec, null, matchingFiles, nonmatchingFiles, untouchableFiles);
        }

        /// <summary>
        /// Runs the test 4 times with the include and exclude using either forward or backward slashes.
        /// Expects the <param name="filespec"></param> and <param name="excludeFileSpects"></param> to contain only backward slashes
        ///
        /// To preserve current MSBuild behaviour, it only does so if the path is not rooted. Rooted paths do not support forward slashes (as observed on MSBuild 14.0.25420.1)
        /// </summary>
        private static void MatchDriverWithDifferentSlashes
            (
            string filespec,
            string[] excludeFilespecs,
            string[] matchingFiles,
            string[] nonmatchingFiles,
            string[] untouchableFiles
            )
        {
            // tests should call this method with backward slashes
            Assert.DoesNotContain(filespec, "/");
            foreach (var excludeFilespec in excludeFilespecs)
            {
                Assert.DoesNotContain(excludeFilespec, "/");
            }

            var forwardSlashFileSpec = Helpers.ToForwardSlash(filespec);
            var forwardSlashExcludeSpecs = excludeFilespecs.Select(Helpers.ToForwardSlash).ToArray();

            MatchDriver(filespec, excludeFilespecs, matchingFiles, nonmatchingFiles, untouchableFiles);
            MatchDriver(filespec, forwardSlashExcludeSpecs, matchingFiles, nonmatchingFiles, untouchableFiles);
            MatchDriver(forwardSlashFileSpec, excludeFilespecs, matchingFiles, nonmatchingFiles, untouchableFiles);
            MatchDriver(forwardSlashFileSpec, forwardSlashExcludeSpecs, matchingFiles, nonmatchingFiles, untouchableFiles);
        }

        private static void MatchDriver(string filespec, string[] excludeFilespecs, string[] matchingFiles, string[] nonmatchingFiles, string[] untouchableFiles, bool normalizeAllPaths = true, bool normalizeExpectedMatchingFiles = false)
        {
            MockFileSystem mockFileSystem = new MockFileSystem(matchingFiles, nonmatchingFiles, untouchableFiles);
            string[] files = FileMatcher.GetFiles
            (
                String.Empty, /* we don't need project directory as we use mock filesystem */
                filespec,
                excludeFilespecs?.ToList(),
                new FileMatcher.GetFileSystemEntries(mockFileSystem.GetAccessibleFileSystemEntries),
                new DirectoryExists(mockFileSystem.DirectoryExists)
            );

            Func<string[], string[]> normalizeAllFunc = (paths => normalizeAllPaths ? paths.Select(MockFileSystem.Normalize).ToArray() : paths);
            Func<string[], string[]> normalizeMatching = (paths => normalizeExpectedMatchingFiles ? paths.Select(MockFileSystem.Normalize).ToArray() : paths);

            string[] normalizedFiles = normalizeAllFunc(files);

            // Validate the matching files.
            if (matchingFiles != null)
            {
                string[] normalizedMatchingFiles = normalizeAllFunc(normalizeMatching(matchingFiles));

                foreach (string matchingFile in normalizedMatchingFiles)
                {
                    int timesFound = 0;
                    foreach (string file in normalizedFiles)
                    {
                        if (String.Compare(file, matchingFile, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            ++timesFound;
                        }
                    }
                    Assert.Equal(1, timesFound);
                }
            }

            // Validate the non-matching files
            if (nonmatchingFiles != null)
            {
                string[] normalizedNonMatchingFiles = normalizeAllFunc(nonmatchingFiles);

                foreach (string nonmatchingFile in normalizedNonMatchingFiles)
                {
                    int timesFound = 0;
                    foreach (string file in normalizedFiles)
                    {
                        if (String.Compare(file, nonmatchingFile, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            ++timesFound;
                        }
                    }
                    Assert.Equal(0, timesFound);
                }
            }

            // Check untouchable files.
            Assert.Equal(0, mockFileSystem.FileHits3); // "At least one file that was marked untouchable was referenced."
        }



        /// <summary>
        /// Simulate GetFileSystemEntries
        /// </summary>
        /// <param name="path"></param>
        /// <param name="pattern"></param>
        /// <returns>Array of matching file system entries (can be empty).</returns>
        private static ImmutableArray<string> GetFileSystemEntriesLoopBack(FileMatcher.FileSystemEntity entityType, string path, string pattern, string projectDirectory, bool stripProjectDirectory)
        {
            return ImmutableArray.Create(Path.Combine(path, pattern));
        }

        /*************************************************************************************
         * Validate that SplitFileSpec(...) is returning the expected constituent values.
         *************************************************************************************/

        private static void ValidateSplitFileSpec
            (
            string filespec,
            string expectedFixedDirectoryPart,
            string expectedWildcardDirectoryPart,
            string expectedFilenamePart
            )
        {
            string fixedDirectoryPart;
            string wildcardDirectoryPart;
            string filenamePart;
            FileMatcher.SplitFileSpec
            (
                filespec,
                out fixedDirectoryPart,
                out wildcardDirectoryPart,
                out filenamePart,
                new FileMatcher.GetFileSystemEntries(GetFileSystemEntriesLoopBack)
            );

            expectedFixedDirectoryPart = FileUtilities.FixFilePath(expectedFixedDirectoryPart);
            expectedWildcardDirectoryPart = FileUtilities.FixFilePath(expectedWildcardDirectoryPart);
            expectedFilenamePart = FileUtilities.FixFilePath(expectedFilenamePart);

            if
                (
                expectedWildcardDirectoryPart != wildcardDirectoryPart
                || expectedFixedDirectoryPart != fixedDirectoryPart
                || expectedFilenamePart != filenamePart
                )
            {
                Console.WriteLine("Expect Fixed '{0}' got '{1}'", expectedFixedDirectoryPart, fixedDirectoryPart);
                Console.WriteLine("Expect Wildcard '{0}' got '{1}'", expectedWildcardDirectoryPart, wildcardDirectoryPart);
                Console.WriteLine("Expect Filename '{0}' got '{1}'", expectedFilenamePart, filenamePart);
                Assert.True(false, "FileMatcher Regression: Failure while validating SplitFileSpec.");
            }
        }

        /*************************************************************************************
        * Given a pattern (filespec) and a candidate filename (fileToMatch). Verify that they
        * do indeed match.
        *************************************************************************************/
        private static void ValidateFileMatch
            (
            string filespec,
            string fileToMatch,
            bool shouldBeRecursive
            )
        {
            ValidateFileMatch(filespec, fileToMatch, shouldBeRecursive, /* Simulate filesystem? */ true);
        }

        /*************************************************************************************
        * Given a pattern (filespec) and a candidate filename (fileToMatch). Verify that they
        * do indeed match.
        *************************************************************************************/
        private static void ValidateFileMatch
            (
            string filespec,
            string fileToMatch,
            bool shouldBeRecursive,
            bool fileSystemSimulation
            )
        {
            if (!IsFileMatchAssertIfIllegal(filespec, fileToMatch, shouldBeRecursive))
            {
                Assert.True(false, "FileMatcher Regression: Failure while validating that files match.");
            }

            // Now, simulate a filesystem with only fileToMatch. Make sure the file exists that way.
            if (fileSystemSimulation)
            {
                MatchDriver
                (
                    filespec,
                    new string[] { fileToMatch },
                    null,
                    null
                );
            }
        }

        /*************************************************************************************
        * Given a pattern (filespec) and a candidate filename (fileToMatch). Verify that they
        * DON'T match.
        *************************************************************************************/
        private static void ValidateNoFileMatch
            (
            string filespec,
            string fileToMatch,
            bool shouldBeRecursive
            )
        {
            if (IsFileMatchAssertIfIllegal(filespec, fileToMatch, shouldBeRecursive))
            {
                Assert.True(false, "FileMatcher Regression: Failure while validating that files don't match.");
            }

            // Now, simulate a filesystem with only fileToMatch. Make sure the file doesn't exist that way.
            MatchDriver
            (
                filespec,
                null,
                new string[] { fileToMatch },
                null
            );
        }

        /*************************************************************************************
        * Verify that the given filespec is illegal.
        *************************************************************************************/
        private static void ValidateIllegal
            (
            string filespec
            )
        {
            Regex regexFileMatch;
            bool needsRecursion;
            bool isLegalFileSpec;
            FileMatcher.GetFileSpecInfoWithRegexObject
            (
                filespec,
                out regexFileMatch,
                out needsRecursion,
                out isLegalFileSpec,
                new FileMatcher.GetFileSystemEntries(GetFileSystemEntriesLoopBack)
            );

            if (isLegalFileSpec)
            {
                Assert.True(false, "FileMatcher Regression: Expected an illegal filespec, but got a legal one.");
            }

            // Now, FileMatcher is supposed to take any legal file name and just return it immediately.
            // Let's see if it does.
            MatchDriver
            (
                filespec,                        // Not legal.
                new string[] { filespec },        // Should match
                null,
                null
            );
        }
        /*************************************************************************************
        * Given a pattern (filespec) and a candidate filename (fileToMatch) return true if
        * FileMatcher would say that they match.
        *************************************************************************************/
        private static bool IsFileMatchAssertIfIllegal
        (
            string filespec,
            string fileToMatch,
            bool shouldBeRecursive
        )
        {
            FileMatcher.Result match = FileMatcher.FileMatch(filespec, fileToMatch);

            if (!match.isLegalFileSpec)
            {
                Console.WriteLine("Checking FileSpec: '{0}' against '{1}'", filespec, fileToMatch);
                Assert.True(false, "FileMatcher Regression: Invalid filespec.");
            }
            if (shouldBeRecursive != match.isFileSpecRecursive)
            {
                Console.WriteLine("Checking FileSpec: '{0}' against '{1}'", filespec, fileToMatch);
                Assert.True(shouldBeRecursive); // "FileMatcher Regression: Match was recursive when it shouldn't be."
                Assert.False(shouldBeRecursive); // "FileMatcher Regression: Match was not recursive when it should have been."
            }
            return match.isMatch;
        }

        #endregion
    }
}




