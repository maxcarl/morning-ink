# Input: GHCR package versions. Output: digests of "latest" and of the newest $keep releases (x.y.z tags).
[ .[] | {name, tags: .metadata.container.tags}
      | .release = ([.tags[] | select(test("^[0-9]+\\.[0-9]+\\.[0-9]+$"))] | first) ]
| ([ .[] | select(.release) ] | sort_by(.release | split(".") | map(tonumber)) | reverse | .[:$keep])
  + [ .[] | select(.tags | index("latest")) ]
| unique_by(.name) | .[].name
