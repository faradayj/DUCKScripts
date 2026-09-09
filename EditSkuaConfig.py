import argparse
import os
import re
import sys
import subprocess

def get_options_storage_name(cs_file_path):
    with open(cs_file_path, 'r', encoding='utf-8') as f:
        content = f.read()
        
    match = re.search(r'OptionsStorage\s*=\s*["\']([^"\']+)["\']', content)
    if match:
        return match.group(1)
    
    return None

def find_script_by_tag(target, scripts_dir):
    matches = []
    for root, _, files in os.walk(scripts_dir):
        for file in files:
            if not file.endswith('.cs'):
                continue
            
            filepath = os.path.join(root, file)
            try:
                with open(filepath, 'r', encoding='utf-8') as f:
                    content = f.read(2048)
                    
                tags_match = re.search(r'tags:\s*(.+)', content, re.IGNORECASE)
                name_match = re.search(r'name:\s*(.+)', content, re.IGNORECASE)
                
                matched = False
                if tags_match:
                    tags = [t.strip().lower() for t in tags_match.group(1).split(',')]
                    if target.lower() in tags:
                        matched = True
                        
                if not matched and name_match:
                    name = name_match.group(1).strip().lower()
                    if target.lower() in name:
                        matched = True
                        
                if matched:
                    matches.append(filepath)
            except Exception:
                pass
                
    return matches

def update_config(config_path, settings):
    options = {}
    
    if os.path.exists(config_path):
        with open(config_path, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if line.startswith("Options:"):
                    parts = line[8:].split('=', 1)
                    if len(parts) == 2:
                        options[parts[0]] = parts[1]
    
    for key, value in settings.items():
        options[key] = value
        
    with open(config_path, 'w', encoding='utf-8') as f:
        for key, value in options.items():
            f.write(f"Options:{key}={value}\n")
            
    print(f"Successfully updated {config_path}")

def main():
    parser = argparse.ArgumentParser(description='Edit Skua script options.')
    parser.add_argument('target', nargs='+', help='Path to the C# script (.cs) OR a tag/name to search for')
    parser.add_argument('--set', action='append', help='Key=Value pair to set (can be used multiple times)', default=[])
    
    args = parser.parse_args()
    
    skua_dir = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
    scripts_dir = os.path.join(skua_dir, 'Scripts')
    skua_options_dir = os.path.join(skua_dir, 'options')
    
    if not os.path.exists(skua_options_dir):
        appdata = os.getenv('APPDATA')
        if appdata:
            skua_options_dir = os.path.join(appdata, 'Skua', 'options')
            scripts_dir = os.path.join(appdata, 'Skua', 'Scripts')
            
    if not os.path.exists(skua_options_dir):
        os.makedirs(skua_options_dir, exist_ok=True)
        
    target_script = " ".join(args.target)
    
    # Resolve script path
    if not os.path.isfile(target_script):
        print(f"Searching for scripts matching '{target_script}'...")
        matches = find_script_by_tag(target_script, scripts_dir)
        
        if not matches:
            print(f"Error: Could not find any scripts matching '{target_script}'")
            sys.exit(1)
            
        if len(matches) == 1:
            target_script = matches[0]
            print(f"Found exactly one match: {os.path.relpath(target_script, scripts_dir)}")
        else:
            print(f"\nMultiple scripts found for '{target_script}':")
            for i, match in enumerate(matches):
                print(f"[{i+1}] {os.path.relpath(match, scripts_dir)}")
                
            choice = input("\nEnter the number of the script to configure (or 'q' to quit): ")
            if choice.lower() == 'q':
                sys.exit(0)
                
            try:
                idx = int(choice) - 1
                if idx < 0 or idx >= len(matches):
                    raise ValueError()
                target_script = matches[idx]
            except ValueError:
                print("Invalid selection.")
                sys.exit(1)
                
    storage_name = get_options_storage_name(target_script)
    if not storage_name:
        print(f"Error: Could not find OptionsStorage variable in {target_script}")
        sys.exit(1)
        
    print(f"Resolved OptionsStorage: {storage_name}")
    config_path = os.path.join(skua_options_dir, f"{storage_name}.cfg")
    
    if not args.set:
        print(f"No --set arguments provided. Opening {config_path} in default text editor...")
        if not os.path.exists(config_path):
            with open(config_path, 'w', encoding='utf-8') as f:
                pass # Create empty file so it can be opened
        # Open in default editor (Windows)
        os.startfile(config_path)
    else:
        settings = {}
        for item in args.set:
            if '=' not in item:
                print(f"Error: Invalid --set argument '{item}'. Must be in Key=Value format.")
                sys.exit(1)
            key, value = item.split('=', 1)
            settings[key] = value
            
        update_config(config_path, settings)

if __name__ == '__main__':
    main()
