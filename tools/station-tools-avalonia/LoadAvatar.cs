using System.IO;

namespace StationToolsAvalonia
{
    // Verbatim port of tools/station-tools/LoadAvatar.cs.
    // This is NOT a UI form — it's a binary parser that reads an EnB
    // avatar template file (offset 44 → avatarType / race / etc., then
    // a long flat list of single-byte appearance attributes and 3-float
    // RGB tuples). Byte/field order MUST match the original because the
    // server reads the exact same blob out of starbase_npc_avatar_templates.
    class LoadAvatar
    {
        public int avatarType;
        public byte avaterVersion;
        public int race;
        public int profession;
        public int gender;
        public int moodType;
        public byte personality;
        public byte nlp;
        public byte bodyType;
        public byte pantsType;
        public byte headType;
        public byte hairNum;
        public byte earNum;
        public byte goggleNum;
        public byte beardNum;
        public byte weaponHipNum;
        public byte weaponUniqueNum;
        public byte weaponBackNum;
        public byte headTextureNum;
        public byte tattooTextureNum;
        public float[] tattooOffset = new float[3];
        public float[] hairColor = new float[3];
        public float[] beardColor = new float[3];
        public float[] eyeColor = new float[3];
        public float[] skinColor = new float[3];
        public float[] shirtPrimaryColor = new float[3];
        public float[] shirtSecondaryColor = new float[3];
        public float[] pantsPrimaryColor = new float[3];
        public float[] pantsSecondaryColor = new float[3];
        public int shirtPrimaryMetal;
        public int shirtSecondarymetal;
        public int pantsPrimaryMetal;
        public int pantsSecondaryMetal;
        public float[] bodyWeight = new float[5];
        public float[] headWeight = new float[5];

        public LoadAvatar(Stream file)
        {
            var bw = new BinaryReader(file);
            bw.BaseStream.Position = 44;

            avatarType = bw.ReadInt32();
            avaterVersion = bw.ReadByte();
            race = bw.ReadInt32();
            profession = bw.ReadInt32();
            gender = bw.ReadInt32();
            moodType = bw.ReadInt32();
            personality = bw.ReadByte();
            nlp = bw.ReadByte();
            bodyType = bw.ReadByte();
            pantsType = bw.ReadByte();
            headType = bw.ReadByte();
            hairNum = bw.ReadByte();
            earNum = bw.ReadByte();
            goggleNum = bw.ReadByte();
            beardNum = bw.ReadByte();
            weaponHipNum = bw.ReadByte();
            weaponUniqueNum = bw.ReadByte();
            weaponBackNum = bw.ReadByte();
            headTextureNum = bw.ReadByte();
            tattooTextureNum = bw.ReadByte();

            for (int i = 0; i < 3; i++) tattooOffset[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) hairColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) beardColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) eyeColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) skinColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) shirtPrimaryColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) shirtSecondaryColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) pantsPrimaryColor[i] = bw.ReadSingle();
            for (int i = 0; i < 3; i++) pantsSecondaryColor[i] = bw.ReadSingle();

            shirtPrimaryMetal = bw.ReadInt32();
            shirtSecondarymetal = bw.ReadInt32();
            pantsPrimaryMetal = bw.ReadInt32();
            pantsSecondaryMetal = bw.ReadInt32();

            for (int i = 0; i < 5; i++) bodyWeight[i] = bw.ReadSingle();
            for (int i = 0; i < 5; i++) headWeight[i] = bw.ReadSingle();
        }
    }
}
