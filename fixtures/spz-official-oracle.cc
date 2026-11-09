// Generates synthetic fixtures with the official MIT SPZ implementation.
// Reference revision and SHA256 outputs are recorded in spz-official-provenance.json.
#include "load-spz.h"
#include "coordinate-system-adobe.h"
#include <cmath>
#include <fstream>
#include <iomanip>
#include <string>
using namespace spz;
int main(int argc,char**argv) {
  std::string folder=argc>1?argv[1]:".";
  GaussianCloud cloud;
  cloud.numPoints=3; cloud.shDegree=3; cloud.antialiased=true;
  cloud.positions={1.25f,-2.5f,3.75f,-10.75f,20.5f,0.125f,100.f,0.f,-200.f};
  cloud.scales={-2.f,0.f,2.f,-1.5f,.25f,1.f,0.f,-3.f,.5f};
  cloud.rotations={0.f,0.f,0.f,1.f,.2f,-.3f,.4f,std::sqrt(.71f),-std::sqrt(.5f),0.f,std::sqrt(.5f),0.f};
  cloud.alphas={-1000.f,0.f,1000.f};
  cloud.colors={-1.f,.25f,2.f,0.f,-2.f,.5f,1.25f,.75f,-.125f};
  for (int i=0;i<135;i++) cloud.sh.push_back(.125f*(i%5-2));
  auto generate=[&](int version,int coordinates) {
    PackOptions pack; pack.version=version; pack.from=CoordinateSystem::RUB;
    cloud.extensions.clear();
    if(coordinates) {auto ext=std::make_shared<SpzExtensionCoordinateSystemAdobe>();ext->coordinateSystem=static_cast<CoordinateSystem>(coordinates);cloud.extensions.push_back(ext);}
    std::string stem=folder+"/spz-official-v"+std::to_string(version)+(coordinates?"-coordinate"+std::to_string(coordinates):"");
    if(!saveSpz(cloud,pack,stem+".spz")) return 2;
    GaussianCloud decoded=loadSpz(stem+".spz",UnpackOptions{});
    UnpackOptions worldOptions;worldOptions.to=CoordinateSystem::RUB;
    GaussianCloud world=loadSpz(stem+".spz",worldOptions);
    if(decoded.numPoints!=3||world.numPoints!=3)return 3;
    std::ofstream out(stem+".json");out<<std::setprecision(9)<<"{";
    auto array=[&](const char*name,const std::vector<float>&values,bool comma=true){out<<"\""<<name<<"\":[";for(size_t i=0;i<values.size();i++){if(i)out<<",";out<<values[i];}out<<"]";if(comma)out<<",";};
    array("positions",decoded.positions);array("scales",decoded.scales);array("rotations",decoded.rotations);array("colors",decoded.colors);array("sh",decoded.sh);
    array("worldPositions",world.positions);array("worldRotations",world.rotations);array("worldSh",world.sh,false);out<<"}";
    return 0;
  };
  for(int version=2;version<=4;version++)if(generate(version,0))return 2;
  for(int coordinate:{6,10,16})if(generate(4,coordinate))return 3;
}
