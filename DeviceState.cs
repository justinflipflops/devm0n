using System.Xml.Serialization;
using System.Text;
using System;
using System.IO;
using System.Globalization;

namespace devm0n
{

    [XmlRoot(ElementName="datavalues")]
    public class DeviceState
    {
        [XmlElement(ElementName="input0state")]
        public double InputState0 {get;set;}

        [XmlElement(ElementName="input1state")]
        public double InputState1 {get;set;}
        
        [XmlElement(ElementName="input2state")]
        public double InputState2 {get;set;}

        [XmlElement(ElementName="input3state")]
        public double InputState3 {get;set;}

        [XmlElement(ElementName="input4state")]
        public double InputState4 {get;set;}

        [XmlElement(ElementName="input5state")]
        public double InputState5 {get;set;}

        [XmlElement(ElementName="input6state")]
        public double InputState6 {get;set;}

        [XmlElement(ElementName="input7state")]
        public double InputState7 {get;set;}

        [XmlElement(ElementName="powerupflag")]
        public double PowerupFlag {get;set;}

        public override bool Equals(object other)
        {
            return Equals(other as DeviceState);
        }

        public virtual bool Equals(DeviceState other)
        {
            if (other == null) { return false; }
            if (object.ReferenceEquals(this, other)) { return true; }
            return ((this.InputState0 == other.InputState0) && (this.InputState1 == other.InputState1) && (this.InputState2 == other.InputState2)
                 && (this.InputState3 == other.InputState3) && (this.InputState4 == other.InputState4) && (this.InputState5 == other.InputState5)
                 && (this.InputState6 == other.InputState6) && (this.InputState7 == other.InputState7) && (this.PowerupFlag == other.PowerupFlag));
        }

        public override int GetHashCode()
        {
            Int32 hashCode = 352033288;
            hashCode = hashCode * -1521134295 + this.InputState0.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState1.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState2.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState3.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState4.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState5.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState6.GetHashCode();
            hashCode = hashCode * -1521134295 + this.InputState7.GetHashCode();
            hashCode = hashCode * -1521134295 + this.PowerupFlag.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(DeviceState item1, DeviceState item2)
        {
            if (object.ReferenceEquals(item1, item2)) { return true; }
            if ((object)item1 == null || (object)item2 == null) { return false; }
            return ((item1.InputState0 == item2.InputState0) && (item1.InputState1 == item2.InputState1) && (item1.InputState2 == item2.InputState2)
                 && (item1.InputState3 == item2.InputState3) && (item1.InputState4 == item2.InputState4) && (item1.InputState5 == item2.InputState5)
                 && (item1.InputState6 == item2.InputState6) && (item1.InputState7 == item2.InputState7) && (item1.PowerupFlag == item2.PowerupFlag));
        }

        public static bool operator !=(DeviceState item1, DeviceState item2)
        {
            return !((item1.InputState0 == item2.InputState0) && (item1.InputState1 == item2.InputState1) && (item1.InputState2 == item2.InputState2)
                 && (item1.InputState3 == item2.InputState3) && (item1.InputState4 == item2.InputState4) && (item1.InputState5 == item2.InputState5)
                 && (item1.InputState6 == item2.InputState6) && (item1.InputState7 == item2.InputState7) && (item1.PowerupFlag == item2.PowerupFlag));
        }
    }
}